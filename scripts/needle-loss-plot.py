#!/usr/bin/env python3

import html
import math
import re
import sys
from pathlib import Path

NUMBER = r"[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?"
STEP_PATTERN = re.compile(rf"^\s*step\s+(\d+)/(\d+)\s+loss\s+({NUMBER})\s*$", re.IGNORECASE)
EPOCH_PATTERN = re.compile(
    rf"^\s*epoch\s+(\d+)/(\d+)\s+loss\s+({NUMBER})(?:\s+val\s+({NUMBER}))?\s*$",
    re.IGNORECASE,
)


def fail(message: str) -> "None":
    raise SystemExit(f"ERROR: {message}")


def read_losses(log_path: Path):
    try:
        lines = log_path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as error:
        fail(f"Could not read finetune log '{log_path}': {error}")

    step_losses = []
    epoch_train_losses = []
    epoch_val_losses = []
    total_steps = 0
    total_epochs = 0
    loss_like_lines = 0

    for line_number, raw_line in enumerate(lines, start=1):
        if "loss" in raw_line.lower():
            loss_like_lines += 1
        step_match = STEP_PATTERN.match(raw_line)
        epoch_match = EPOCH_PATTERN.match(raw_line)
        match = step_match or epoch_match
        if not match:
            continue

        values = [float(value) for value in match.groups() if value is not None]
        if not all(math.isfinite(value) for value in values):
            fail(f"Non-finite loss value on line {line_number} of '{log_path}'.")

        if step_match:
            step_index, reported_total, loss = int(match.group(1)), int(match.group(2)), float(match.group(3))
            total_steps = max(total_steps, reported_total)
            step_losses.append((float(step_index), loss))
        else:
            epoch_index, reported_total, train_loss = int(match.group(1)), int(match.group(2)), float(match.group(3))
            total_epochs = max(total_epochs, reported_total)
            epoch_train_losses.append((float(epoch_index), train_loss))
            if match.group(4) is not None:
                epoch_val_losses.append((float(epoch_index), float(match.group(4))))

    if not step_losses and not epoch_train_losses:
        detail = "found loss-like text but no supported metrics" if loss_like_lines else "contained no loss metrics"
        fail(
            f"Finetune log '{log_path}' {detail}. Expected lines such as "
            "'step 1/10 loss 0.42' or 'epoch 1/3 loss 0.42 val 0.38'."
        )

    if total_steps <= 0:
        total_steps = int(step_losses[-1][0]) if step_losses else int(epoch_train_losses[-1][0])
    if total_epochs <= 0:
        total_epochs = int(epoch_train_losses[-1][0]) if epoch_train_losses else 1
    return step_losses, epoch_train_losses, epoch_val_losses, total_steps, total_epochs


def nice_ticks(low: float, high: float, target_count: int = 5):
    if low == high:
        padding = max(abs(low) * 0.1, 1.0)
        low, high = low - padding, high + padding
    rough_step = (high - low) / max(target_count, 1)
    magnitude = 10 ** math.floor(math.log10(rough_step)) if rough_step > 0 else 1.0
    step = next(factor * magnitude for factor in (1.0, 2.0, 5.0, 10.0) if rough_step <= factor * magnitude)
    start, end = math.floor(low / step) * step, math.ceil(high / step) * step
    ticks = []
    value = start
    while value <= end + step * 0.5:
        ticks.append(round(value, 10))
        value += step
    return ticks, start, end


def epoch_ticks(total_epochs: int, total_steps: int):
    count = min(max(total_epochs, 1), 6)
    seen, ticks = set(), []
    for index in range(count):
        epoch = int(round(1 + index * (total_epochs - 1) / max(count - 1, 1)))
        label = f"E{epoch}"
        if label not in seen:
            seen.add(label)
            ticks.append((epoch * total_steps / total_epochs, label))
    return ticks


def render(log_path: Path, output_path: Path) -> None:
    step_losses, epoch_train, epoch_val, total_steps, total_epochs = read_losses(log_path)
    epoch_train_points = [(epoch * total_steps / total_epochs, loss) for epoch, loss in epoch_train]
    epoch_val_points = [(epoch * total_steps / total_epochs, loss) for epoch, loss in epoch_val]
    series = [step_losses, epoch_train_points, epoch_val_points]
    x_values = [x for items in series for x, _ in items]
    y_values = [y for items in series for _, y in items]
    x_min, x_max, y_min, y_max = min(x_values), max(x_values), min(y_values), max(y_values)
    if x_min == x_max:
        x_min, x_max = x_min - 1, x_max + 1
    y_ticks, y_axis_min, y_axis_max = nice_ticks(y_min, y_max)

    width, height, left, right, top, bottom = 1280, 720, 96, 40, 92, 96
    plot_width, plot_height = width - left - right, height - top - bottom
    map_x = lambda value: left + (value - x_min) / (x_max - x_min) * plot_width
    map_y = lambda value: top + (y_axis_max - value) / (y_axis_max - y_axis_min) * plot_height

    def polyline(points, color, stroke_width, opacity=1.0):
        if not points:
            return ""
        coordinates = " ".join(f"{map_x(x):.2f},{map_y(y):.2f}" for x, y in points)
        return f'<polyline points="{coordinates}" fill="none" stroke="{color}" stroke-width="{stroke_width}" stroke-linecap="round" stroke-linejoin="round" opacity="{opacity}" />'

    lines = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
        '<rect width="100%" height="100%" fill="#f8fafc" />',
        f'<text x="{left}" y="32" font-family="Segoe UI, Arial, sans-serif" font-size="24" font-weight="700" fill="#0f172a">Needle Fine-tune Loss Curve</text>',
        f'<text x="{left}" y="54" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="#475569">Source: {html.escape(log_path.name)}</text>',
        f'<rect x="{left}" y="{top}" width="{plot_width}" height="{plot_height}" fill="#ffffff" stroke="#cbd5e1" />',
    ]
    for tick in y_ticks:
        y = map_y(tick)
        lines += [
            f'<line x1="{left}" y1="{y:.2f}" x2="{left + plot_width}" y2="{y:.2f}" stroke="#e2e8f0" />',
            f'<text x="{left - 12}" y="{y + 5:.2f}" text-anchor="end" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#475569">{tick:.4f}</text>',
        ]
    for x_value, label in epoch_ticks(total_epochs, total_steps):
        x = map_x(x_value)
        lines += [
            f'<line x1="{x:.2f}" y1="{top}" x2="{x:.2f}" y2="{top + plot_height}" stroke="#f1f5f9" />',
            f'<text x="{x:.2f}" y="{top + plot_height + 28}" text-anchor="middle" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#475569">{label}</text>',
        ]
    lines += [
        polyline(step_losses, "#64748b", 2, 0.8),
        polyline(epoch_train_points, "#d97706", 3),
        polyline(epoch_val_points, "#0f766e", 3),
    ]
    for points, color in ((epoch_train_points, "#d97706"), (epoch_val_points, "#0f766e")):
        lines.extend(f'<circle cx="{map_x(x):.2f}" cy="{map_y(y):.2f}" r="4" fill="{color}" />' for x, y in points)

    legend_items = [("Step loss", "#64748b"), ("Epoch train loss", "#d97706")]
    if epoch_val_points:
        legend_items.append(("Epoch val loss", "#0f766e"))
    legend_width, legend_row_height, legend_padding = 190, 20, 12
    legend_height = legend_padding * 2 + legend_row_height * len(legend_items) - (legend_row_height - 14)
    legend_x = left + plot_width - legend_width - 12
    legend_y = top + 12
    lines.append(
        f'<rect x="{legend_x}" y="{legend_y}" width="{legend_width}" height="{legend_height}" '
        'fill="#ffffff" fill-opacity="0.9" stroke="#cbd5e1" rx="4" />'
    )
    for index, (label, color) in enumerate(legend_items):
        row_y = legend_y + legend_padding + 14 + index * legend_row_height
        lines += [
            f'<line x1="{legend_x + 12}" y1="{row_y - 4}" x2="{legend_x + 32}" y2="{row_y - 4}" '
            f'stroke="{color}" stroke-width="3" stroke-linecap="round" />',
            f'<text x="{legend_x + 40}" y="{row_y}" font-family="Segoe UI, Arial, sans-serif" font-size="12" fill="#334155">{html.escape(label)}</text>',
        ]

    latest = []
    if step_losses:
        latest.append(f"step {int(step_losses[-1][0])} loss {step_losses[-1][1]:.4f}")
    if epoch_train:
        latest.append(f"epoch {int(epoch_train[-1][0])} loss {epoch_train[-1][1]:.4f}")
    if epoch_val:
        latest.append(f"val {epoch_val[-1][1]:.4f}")
    lines += [
        f'<text x="{left}" y="{height - 34}" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="#334155">Epoch progression</text>',
        f'<text x="{left}" y="{top - 12}" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="#334155">Loss</text>',
        f'<text x="{left + plot_width}" y="{height - 34}" text-anchor="end" font-family="Segoe UI, Arial, sans-serif" font-size="13" fill="#475569">{html.escape(" | ".join(latest))}</text>',
        "</svg>",
    ]
    try:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text("\n".join(line for line in lines if line) + "\n", encoding="utf-8")
    except OSError as error:
        fail(f"Could not write SVG '{output_path}': {error}")


def main() -> int:
    if len(sys.argv) != 3:
        fail("Usage: needle-loss-plot.py <finetune-log> <output-svg>")
    log_path, output_path = Path(sys.argv[1]), Path(sys.argv[2])
    if not log_path.is_file():
        fail(f"Finetune log was not found: {log_path}")
    render(log_path, output_path)
    print(output_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
