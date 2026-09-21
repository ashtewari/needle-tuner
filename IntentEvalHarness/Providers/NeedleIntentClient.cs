using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace IntentEvalHarness;

public sealed class NeedleIntentCompletion
{
  public IntentEvaluationResult Result { get; init; } = new();
  public double? ReportedConfidence { get; init; }
  public string RawResponseJson { get; init; } = string.Empty;
  public string ResponseType { get; init; } = string.Empty;
  public string? FunctionName { get; init; }
  public string? ArgumentsJson { get; init; }
  public string DiagnosticKind { get; init; } = "tool-call";
  public string? FallbackReason { get; init; }
}

/// <summary>
/// P/Invoke wrapper around Cactus Needle2's native engine (docs/use-needle2.md).
/// Scoped to this eval harness only; not used by production code.
/// </summary>
public sealed class NeedleIntentClient : IDisposable
{
    private const int BufferSize = 65_536;
  private const string NativeLibraryName = "libneedle";

  private static string? _configuredNativeLibraryPath;

  static NeedleIntentClient()
  {
    NativeLibrary.SetDllImportResolver(typeof(NeedleIntentClient).Assembly, ResolveNativeLibrary);
  }

  public static void Configure(string? nativeLibraryPath)
  {
    _configuredNativeLibraryPath = string.IsNullOrWhiteSpace(nativeLibraryPath)
      ? null
      : nativeLibraryPath.Trim();
  }

  private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
  {
    if (!string.Equals(libraryName, NativeLibraryName, StringComparison.Ordinal))
    {
      return IntPtr.Zero;
    }

    foreach (var candidate in GetNativeLibraryCandidates())
    {
      if (TryLoadNativeLibrary(candidate, assembly, searchPath, out var handle))
      {
        return handle;
      }
    }

    return IntPtr.Zero;
  }

  private static IEnumerable<string> GetNativeLibraryCandidates()
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var candidate in ExpandConfiguredCandidates(_configuredNativeLibraryPath))
    {
      if (seen.Add(candidate))
      {
        yield return candidate;
      }
    }

    foreach (var candidate in new[] { NativeLibraryName, "libneedle.dll", "libneedle.so" })
    {
      if (seen.Add(candidate))
      {
        yield return candidate;
      }
    }

    foreach (var root in GetSearchRoots())
    {
      foreach (var candidate in new[]
           {
             Path.Combine(root, "libneedle.dll"),
             Path.Combine(root, "libneedle.so"),
             Path.Combine(root, "native", "libneedle.dll"),
             Path.Combine(root, "native", "libneedle.so"),
             Path.Combine(root, "native", GetCurrentRid(), GetNativeLibraryFileName()),
             Path.Combine(root, "IntentEvalHarness", "native", GetCurrentRid(), GetNativeLibraryFileName())
           })
      {
        if (seen.Add(candidate))
        {
          yield return candidate;
        }
      }
    }
  }

  private static IEnumerable<string> ExpandConfiguredCandidates(string? configuredPath)
  {
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
      yield break;
    }

    if (!HasDirectorySeparator(configuredPath) && string.IsNullOrWhiteSpace(Path.GetExtension(configuredPath)))
    {
      yield return configuredPath;
    }

    if (Path.IsPathRooted(configuredPath))
    {
      yield return configuredPath;
      yield break;
    }

    foreach (var root in GetSearchRoots())
    {
      yield return Path.Combine(root, configuredPath);
    }
  }

  private static IEnumerable<string> GetSearchRoots()
  {
    var current = AppContext.BaseDirectory;
    for (var depth = 0; depth < 6 && !string.IsNullOrWhiteSpace(current); depth++)
    {
      yield return current;

      var parent = Directory.GetParent(current);
      if (parent is null)
      {
        yield break;
      }

      current = parent.FullName;
    }
  }

  private static bool TryLoadNativeLibrary(string candidate, Assembly assembly, DllImportSearchPath? searchPath, out IntPtr handle)
  {
    handle = IntPtr.Zero;

    if (Path.IsPathRooted(candidate) || HasDirectorySeparator(candidate))
    {
      return File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out handle);
    }

    return NativeLibrary.TryLoad(candidate, assembly, searchPath, out handle);
  }

  public static string GetCurrentRid()
  {
    var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"win-{architecture}" : $"linux-{architecture}";
  }

  public static string GetNativeLibraryFileName() =>
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libneedle.dll" : "libneedle.so";

  private static bool HasDirectorySeparator(string path)
  {
    return path.IndexOf(Path.DirectorySeparatorChar) >= 0 || path.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
  }

    private static class Native
    {
    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int needle_init(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string system,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string toolsJson,
            IntPtr toolIndexPath);

    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int needle_complete(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            int maxNewTokens,
            [Out] byte[] outputBuffer,
            int bufferSize);

    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void needle_reset();

    [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int needle_load(
          [In] byte[] weights,
          int length);
    }

    // Needle performs best when it can choose among concrete actions rather than fill a generic
    // reasoning envelope. Keep the output mapping in C# so the eval harness can still compare
    // against the existing IntentEvaluationResult contract.
    private const string ToolsJson = """
    [
      {
        "name": "search_item",
        "description": "Find a specific item in WhichBox inventory, answer where an item is, tell which box contains it, or tell whether the user has it.",
        "parameters": {
          "type": "object",
          "properties": {
            "itemName": {
              "type": "string",
              "description": "Item the user wants to find"
            },
            "boxLabel": {
              "type": "string",
              "description": "Optional box or container filter"
            }
          },
          "required": [
            "itemName"
          ]
        }
      },
      {
        "name": "add_item",
        "description": "Add a new item to inventory or place an item into a box.",
        "parameters": {
          "type": "object",
          "properties": {
            "itemName": {
              "type": "string",
              "description": "Item the user wants to add"
            },
            "boxLabel": {
              "type": "string",
              "description": "Box or container where the item should go"
            },
            "quantity": {
              "type": "integer",
              "minimum": 1,
              "description": "Quantity to add when specified"
            }
          },
          "required": [
            "itemName"
          ]
        }
      },
      {
        "name": "update_item",
        "description": "Update an existing item, such as changing quantity, renaming it, editing details, or moving it to a new box.",
        "parameters": {
          "type": "object",
          "properties": {
            "itemName": {
              "type": "string",
              "description": "Existing item to update"
            },
            "boxLabel": {
              "type": "string",
              "description": "Destination box when moving the item"
            },
            "quantity": {
              "type": "integer",
              "minimum": 0,
              "description": "New quantity when specified"
            },
            "newName": {
              "type": "string",
              "description": "New item name when renaming"
            }
          },
          "required": [
            "itemName"
          ]
        }
      },
      {
        "name": "delete_item",
        "description": "Delete or remove an item entry from inventory.",
        "parameters": {
          "type": "object",
          "properties": {
            "itemName": {
              "type": "string",
              "description": "Item to remove"
            },
            "boxLabel": {
              "type": "string",
              "description": "Optional box or container context"
            }
          },
          "required": [
            "itemName"
          ]
        }
      },
      {
        "name": "manage_box",
        "description": "Create, rename, delete, merge, or otherwise manage boxes and containers.",
        "parameters": {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": [
                "create",
                "rename",
                "delete",
                "merge",
                "other"
              ],
              "description": "Box management action"
            },
            "boxLabel": {
              "type": "string",
              "description": "Primary box or container name"
            },
            "newBoxLabel": {
              "type": "string",
              "description": "New box name when renaming"
            },
            "secondaryBoxLabel": {
              "type": "string",
              "description": "Second box when merging"
            }
          },
          "required": [
            "action"
          ]
        }
      },
      {
        "name": "view_inventory",
        "description": "Show inventory contents, reports, counts, or the contents of a box.",
        "parameters": {
          "type": "object",
          "properties": {
            "scope": {
              "type": "string",
              "enum": [
                "all_items",
                "all_boxes",
                "box_contents",
                "count",
                "report"
              ],
              "description": "Inventory view the user is requesting"
            },
            "boxLabel": {
              "type": "string",
              "description": "Box or container to inspect"
            },
            "location": {
              "type": "string",
              "description": "Optional area such as garage or storage"
            }
          },
          "required": [
            "scope"
          ]
        }
      },
      {
        "name": "upload_photo",
        "description": "Upload, take, scan, or attach a photo, image, or receipt for an item or box.",
        "parameters": {
          "type": "object",
          "properties": {
            "itemName": {
              "type": "string",
              "description": "Item to attach the image to"
            },
            "boxLabel": {
              "type": "string",
              "description": "Box to attach the image to"
            },
            "imageKind": {
              "type": "string",
              "enum": [
                "photo",
                "image",
                "receipt"
              ],
              "description": "Type of image when the user makes it explicit"
            }
          }
        }
      },
      {
        "name": "general_help",
        "description": "Explain how WhichBox works, what features it offers, how to get started, what help is available, or how to organize and label boxes.",
        "parameters": {
          "type": "object",
          "properties": {
            "topic": {
              "type": "string",
              "description": "Optional help topic such as features, labeling, getting started, or capabilities"
            }
          }
        }
      }
    ]
    """;

    private const string SystemPrompt =
        "You route user requests for WhichBox, a digital inventory application. " +
        "Choose the single best action tool for the user's request. " +
        "Use only arguments grounded in the user's words. " +
      "Requests about where an item is or whether the user has it should use search_item. " +
      "Requests to upload, attach, scan, or take a picture should use upload_photo. " +
      "Questions about how WhichBox works, its features, or getting started should use general_help. " +
        "If no tool fits or the request is too ambiguous, return an empty call.";

    private bool _initialized;
    private byte[]? _loadedWeightsBytes;

    public string ActiveWeightsLabel { get; private set; } = "Needle Base";
    public string? LoadedWeightsPath { get; private set; }
    public bool HasLoadedExternalWeights => !string.IsNullOrWhiteSpace(LoadedWeightsPath);

    public static string GetToolsJson()
    {
      return ToolsJson;
    }

    public NeedleIntentClient()
    {
        Initialize();
    }

      public NeedleIntentClient(NeedleWeightArtifact artifact)
      {
        ArgumentNullException.ThrowIfNull(artifact);

        _loadedWeightsBytes = File.ReadAllBytes(artifact.WeightsPath);
        LoadWeightsBlob(_loadedWeightsBytes, artifact.DisplayName);
        Initialize();

        ActiveWeightsLabel = artifact.DisplayName;
        LoadedWeightsPath = artifact.WeightsPath;
      }

      public void LoadWeights(NeedleWeightArtifact artifact)
      {
        ArgumentNullException.ThrowIfNull(artifact);

        _loadedWeightsBytes = File.ReadAllBytes(artifact.WeightsPath);
        LoadWeightsBlob(_loadedWeightsBytes, artifact.DisplayName);

        ActiveWeightsLabel = artifact.DisplayName;
        LoadedWeightsPath = artifact.WeightsPath;
      }

      private void Initialize()
      {
        int rc = Native.needle_init(SystemPrompt, ToolsJson, IntPtr.Zero);
        if (rc < 0)
        {
            throw new InvalidOperationException($"needle_init failed: {rc}");
        }

        _initialized = true;
      }

      private static void LoadWeightsBlob(byte[] weights, string displayName)
      {
        var rc = Native.needle_load(weights, weights.Length);
        if (rc < 0)
        {
          throw new InvalidOperationException($"needle_load failed for '{displayName}': {rc}");
        }
      }

    public IntentEvaluationResult Complete(string input, int maxNewTokens = 256)
    {
      return CompleteWithDiagnostics(input, maxNewTokens).Result;
    }

    public NeedleIntentCompletion CompleteWithDiagnostics(string input, int maxNewTokens = 256)
    {
        var buffer = new byte[BufferSize];

        int rc = Native.needle_complete(input, maxNewTokens, buffer, buffer.Length);
        if (rc < 0)
        {
            throw new InvalidOperationException($"needle_complete failed: {rc}");
        }

        int length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        string json = Encoding.UTF8.GetString(buffer, 0, length);
        try
        {
          using var response = JsonDocument.Parse(json);
          var root = response.RootElement;

          var rawConfidence = root.TryGetProperty("confidence", out var confElement) && confElement.ValueKind == JsonValueKind.Number
            ? confElement.GetDouble()
            : (double?)null;
          var reportedConfidence = HasLoadedExternalWeights ? null : rawConfidence;
          var responseType = root.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;

          if (!string.Equals(responseType, "call", StringComparison.OrdinalIgnoreCase))
          {
            return BuildFallback(
              json,
              responseType,
              reportedConfidence,
              diagnosticKind: "non-call",
              fallbackReason: "Needle did not return a function call");
          }

          if (!root.TryGetProperty("function_calls", out var functionCalls) || functionCalls.ValueKind != JsonValueKind.Array)
          {
            return BuildFallback(
              json,
              responseType,
              reportedConfidence,
              diagnosticKind: "missing-function-calls",
              fallbackReason: "Needle response did not include a function_calls array");
          }

          if (functionCalls.GetArrayLength() == 0)
          {
            return BuildFallback(
              json,
              responseType,
              reportedConfidence,
              diagnosticKind: "empty-call",
              fallbackReason: "Needle returned no function calls");
          }

          var firstCall = functionCalls[0];
          var functionName = firstCall.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;

          if (!firstCall.TryGetProperty("arguments", out var arguments))
          {
            return BuildFallback(
              json,
              responseType,
              reportedConfidence,
              functionName,
              diagnosticKind: "missing-arguments",
              fallbackReason: "Needle call did not include arguments");
          }

          var argumentsJson = arguments.GetRawText();
          var intent = MapToolNameToIntent(functionName);
          if (intent is null)
          {
            return BuildFallback(
              json,
              responseType,
              reportedConfidence,
              functionName,
              argumentsJson,
              diagnosticKind: "unknown-tool",
              fallbackReason: $"Needle selected an unknown tool '{functionName}'");
          }

          var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
            argumentsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

          if (parameters is null)
          {
            return BuildFallback(
              json,
              responseType,
              reportedConfidence,
              functionName,
              argumentsJson,
              diagnosticKind: "parse-fallback",
              fallbackReason: "Failed to parse Needle arguments");
          }

          var reasoning = root.TryGetProperty("reasoning", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String
            ? reasoningElement.GetString() ?? string.Empty
            : string.Empty;

          var result = new IntentEvaluationResult
          {
            Intent = intent,
            Confidence = reportedConfidence ?? 0,
            Parameters = new Dictionary<string, object>(parameters, StringComparer.OrdinalIgnoreCase),
            Reasoning = reasoning,
            SuggestedActions = BuildSuggestedActions(intent)
          };

          return new NeedleIntentCompletion
          {
            Result = result,
            ReportedConfidence = reportedConfidence,
            RawResponseJson = json,
            ResponseType = responseType,
            FunctionName = functionName,
            ArgumentsJson = argumentsJson,
            DiagnosticKind = "tool-call"
          };
        }
        catch (JsonException ex)
        {
          return new NeedleIntentCompletion
            {
            Result = new IntentEvaluationResult
            {
              Intent = "UNCLEAR",
              Confidence = 0.1,
              Reasoning = $"Needle response JSON parsing failed: {ex.Message}"
            },
            RawResponseJson = json,
            DiagnosticKind = "invalid-json",
            FallbackReason = $"Needle response JSON parsing failed: {ex.Message}"
            };
        }
      }

      private static NeedleIntentCompletion BuildFallback(
        string rawResponseJson,
        string responseType,
        double? confidence,
        string? functionName = null,
        string? argumentsJson = null,
        string diagnosticKind = "fallback",
        string fallbackReason = "Needle fallback")
      {
        return new NeedleIntentCompletion
        {
          Result = new IntentEvaluationResult
          {
            Intent = "UNCLEAR",
            Confidence = confidence ?? 0.1,
            Reasoning = fallbackReason
          },
          ReportedConfidence = confidence,
          RawResponseJson = rawResponseJson,
          ResponseType = responseType,
          FunctionName = functionName,
          ArgumentsJson = argumentsJson,
          DiagnosticKind = diagnosticKind,
          FallbackReason = fallbackReason
        };
    }

      internal static string? MapToolNameToIntent(string? functionName)
      {
        return functionName switch
        {
          "search_item" => "SEARCH_ITEM",
          "add_item" => "ADD_ITEM",
          "update_item" => "UPDATE_ITEM",
          "delete_item" => "DELETE_ITEM",
          "manage_box" => "MANAGE_BOX",
          "view_inventory" => "VIEW_INVENTORY",
          "upload_photo" => "UPLOAD_PHOTO",
          "general_help" => "GENERAL_HELP",
          _ => null
        };
      }

      private static List<string> BuildSuggestedActions(string intent)
      {
        return intent switch
        {
          "SEARCH_ITEM" => ["Search the inventory"],
          "ADD_ITEM" => ["Add the item to inventory"],
          "UPDATE_ITEM" => ["Update the existing item"],
          "DELETE_ITEM" => ["Delete the item entry"],
          "MANAGE_BOX" => ["Manage the selected box"],
          "VIEW_INVENTORY" => ["Show inventory details"],
          "UPLOAD_PHOTO" => ["Attach the image"],
          "GENERAL_HELP" => ["Provide WhichBox guidance"],
          _ => []
        };
      }

    public void Reset()
    {
        if (_initialized)
        {
            Native.needle_reset();
        }
    }

    public void Dispose()
    {
        Reset();
    }
}
