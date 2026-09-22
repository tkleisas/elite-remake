using EliteDataExtractor.Assembly;
using EliteDataExtractor.Extraction;
using EliteDataExtractor.Verification;

namespace EliteDataExtractor;

/// <summary>
/// Command line entry point.
///
///   EliteDataExtractor [all|extract|verify] [--source &lt;library&gt;] [--out &lt;directory&gt;] [--quiet]
///
/// "all" (the default command) extracts the ship blueprints from the original assembly, writes
/// data/ships.json and then verifies the result against the reference binaries.
/// </summary>
internal static class Program
{
    private const string DefaultSourceLibrary = "/home/tkleisas/Projects/elite-source-code-library";
    private const string SourceLibraryEnvironmentVariable = "ELITE_SOURCE_LIBRARY";
    private const string DefaultOutputDirectory = "data";
    private const string JsonFileName = "ships.json";

    private static int Main(string[] args)
    {
        CliOptions? options = CliOptions.Parse(args);
        if (options is null)
        {
            return CliOptions.HelpRequested ? 0 : 2;
        }

        string missileBinary = Path.Combine(
            options.Source,
            ShipExtractor.BinaryDirectory.Replace('/', Path.DirectorySeparatorChar),
            "MISSILE.bin");

        try
        {
            ExtractionResult extraction = new ShipExtractor(options.Source, options.Log).Extract();

            string jsonPath = options.JsonPath;
            if (options.Command is "extract" or "all")
            {
                ShipJsonWriter.Write(extraction.Document, jsonPath);
                Console.WriteLine($"Wrote {jsonPath} ({new FileInfo(jsonPath).Length} bytes)");
                PrintSummary(extraction);
            }

            if (options.Command is "verify" or "all")
            {
                var report = new ShipBinaryVerifier(options.Source, Console.Out).Verify(extraction, missileBinary);
                Console.WriteLine();
                Console.WriteLine($"Verification: {report.FilesCompared} ship files, {report.SlotsCompared} XX21 slots, "
                    + $"{report.ShipsCompared} ship blueprints compared, {report.Mismatches.Count} mismatch(es)");
                foreach (string note in report.Notes)
                {
                    Console.WriteLine($"  note: {note}");
                }

                foreach (string mismatch in report.Mismatches)
                {
                    Console.Error.WriteLine($"  MISMATCH: {mismatch}");
                }

                if (!report.Passed)
                {
                    Console.Error.WriteLine("Verification FAILED");
                    return 1;
                }

                Console.WriteLine("Verification PASSED");
            }

            return 0;
        }
        catch (AsmException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 3;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 3;
        }
    }

    private static void PrintSummary(ExtractionResult extraction)
    {
        ShipDataDocument document = extraction.Document;
        Console.WriteLine(
            $"Extracted {document.Counts.Ships} ships, {document.Counts.RegisteredTypes} registered ship types, "
            + $"{document.Counts.ShipSets} ship sets ({document.Counts.ShipSetEntries} registrations)");
        Console.WriteLine(
            $"  {document.Counts.Vertices} vertices, {document.Counts.Edges} edges, {document.Counts.Faces} faces");

        foreach (string note in document.Notes)
        {
            Console.WriteLine($"  note: {note}");
        }
    }

    private sealed class CliOptions
    {
        public string Command { get; private init; } = "all";

        public string Source { get; private init; } = DefaultSourceLibrary;

        public string OutputDirectory { get; private init; } = DefaultOutputDirectory;

        public bool Quiet { get; private init; }

        public string JsonPath => OutputDirectory.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? OutputDirectory
            : Path.Combine(OutputDirectory, JsonFileName);

        public Action<string> Log => Quiet ? Noop : Console.WriteLine;

        public static bool HelpRequested { get; private set; }

        private static void Noop(string message)
        {
            _ = message;
        }

        public static CliOptions? Parse(string[] args)
        {
            string command = "all";
            string source = Environment.GetEnvironmentVariable(SourceLibraryEnvironmentVariable) ?? DefaultSourceLibrary;
            string output = DefaultOutputDirectory;
            bool quiet = false;
            bool commandSeen = false;

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                string? value = null;
                int equals = argument.IndexOf('=');
                if (argument.StartsWith("--", StringComparison.Ordinal) && equals > 0)
                {
                    value = argument[(equals + 1)..];
                    argument = argument[..equals];
                }

                switch (argument)
                {
                    case "all":
                    case "extract":
                    case "verify":
                        if (commandSeen)
                        {
                            Console.Error.WriteLine($"error: unexpected extra command '{argument}'");
                            return null;
                        }

                        command = argument;
                        commandSeen = true;
                        break;

                    case "--source":
                    {
                        string? parsed = value ?? Next(args, ref i);
                        if (parsed is null)
                        {
                            Console.Error.WriteLine("error: --source needs a path");
                            return null;
                        }

                        source = parsed;
                        break;
                    }

                    case "--out":
                    {
                        string? parsed = value ?? Next(args, ref i);
                        if (parsed is null)
                        {
                            Console.Error.WriteLine("error: --out needs a path");
                            return null;
                        }

                        output = parsed;
                        break;
                    }

                    case "--quiet":
                    case "-q":
                        quiet = true;
                        break;

                    case "--help":
                    case "-h":
                        HelpRequested = true;
                        PrintUsage();
                        return null;

                    default:
                        Console.Error.WriteLine($"error: unknown argument '{argument}'");
                        PrintUsage();
                        return null;
                }
            }

            if (!Directory.Exists(source))
            {
                Console.Error.WriteLine($"error: source library not found: {source}");
                return null;
            }

            return new CliOptions
            {
                Command = command,
                Source = Path.GetFullPath(source),
                OutputDirectory = output,
                Quiet = quiet,
            };
        }

        private static string? Next(string[] args, ref int index) =>
            index + 1 < args.Length ? args[++index] : null;

        private static void PrintUsage()
        {
            Console.WriteLine(
                """
                EliteDataExtractor - extracts Elite ship blueprints from the original 6502 assembly.

                Usage:
                  EliteDataExtractor [all|extract|verify] [options]

                Commands:
                  all       extract, write the JSON, then verify (default)
                  extract   extract and write the JSON only
                  verify    decode the reference binaries and compare them with the extracted data

                Options:
                  --source <path>   Mark Moxon's elite-source-code-library
                                    (default: $ELITE_SOURCE_LIBRARY or
                                     /home/tkleisas/Projects/elite-source-code-library)
                  --out <path>      output directory, or a .json file path (default: data)
                  --quiet, -q       suppress progress output
                  --help, -h        show this help
                """);
        }
    }
}
