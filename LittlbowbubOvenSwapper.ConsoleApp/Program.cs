using LlamaLogic.Packages;
using System.Text;
using System.Threading.Tasks.Dataflow;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using static System.Console;

namespace LittlbowbubOvenSwapper.ConsoleApp;

internal class Program
{
    static readonly IReadOnlyDictionary<ulong, string> ovens = new Dictionary<ulong, string>
    {
        { 15604, "phaseMulti_Food-OvenBasic" },
        { 37792, "phaseMulti_Food-OvenGourmet" },
        { 270727, "phaseMulti_Food-OvenBasic_OffTheGrid" }
    };

    static readonly XmlWriterSettings tuningMarkupWriterSettings = new()
    {
        Indent = true,
        OmitXmlDeclaration = false,
        Encoding = Encoding.UTF8
    };

    static async Task Main(string[] args)
    {
        WriteLine("Littlbowbub's Oven Swapper");
        var replacingOven = QuitIfNull(SelectOven(ovens, "Which oven am I replacing?"));
        var substitutionOven = QuitIfNull(SelectOven(ovens, "Which oven am I substituting in for the one I'm replacing?"));
        var substitutionOvenId = substitutionOven.ToString();
        var substitutionOvenName = ovens[substitutionOven];
        var outputDirectory = QuitIfNull(SelectDirectory("Where should I be outputting packages I've changed?"));
        var replacingOvenXPathQuery = $"./I[@i = 'recipe' and @c = 'Recipe']/L[@n = '_phases']/U/V[@n = 'value' and @t = 'multi_stage_phase_ref']/U[@n = 'multi_stage_phase_ref']/T[@n = 'factory' and normalize-space(text()) = '{replacingOven}']";
        while (true)
        {
            var inputDirectory = SelectDirectory("What's a directory containing packages for me to examine?");
            if (inputDirectory is null)
            {
                WriteLine("Alright, I suppose my work is done. Have a nice day!");
                break;
            }
            var broadcastRecipePackageFile = new BroadcastBlock<FileInfo>(packageFile => packageFile);
            var examineRecipePackageFile = new ActionBlock<FileInfo>(packageFile =>
            {
                WriteLine($"  Examining {packageFile.FullName[(inputDirectory.FullName.Length + 1)..]}");
                using var packageStream = packageFile.OpenRead();
                using var package = Package.FromStream(packageStream);
                var recipesAltered = false;
                foreach (var recipeTuningKey in package.GetResourceKeys().Where(key => key.Type is PackageResourceType.RecipeTuning))
                {
                    var tuningMarkup = XDocument.Parse(Encoding.UTF8.GetString(package.GetResourceContent(recipeTuningKey).Span));
                    var affectedRecipeMultiStagePhaseFactories = tuningMarkup.XPathSelectElements(replacingOvenXPathQuery).ToList().AsReadOnly();
                    if (affectedRecipeMultiStagePhaseFactories.Count > 0)
                    {
                        if (package.GetResourceNameByKey(recipeTuningKey) is { } recipeTuningName)
                            WriteLine($"  Making oven substitutions in {package.GetResourceNameByKey(recipeTuningKey)} ({recipeTuningKey.Instance})");
                        else
                            WriteLine($"  Making oven substitutions in {recipeTuningKey}");
                        foreach (XElement affectedRecipeMultiStagePhaseFactory in affectedRecipeMultiStagePhaseFactories)
                        {
                            affectedRecipeMultiStagePhaseFactory.RemoveNodes();
                            affectedRecipeMultiStagePhaseFactory.Add(new XText(substitutionOvenId));
                            affectedRecipeMultiStagePhaseFactory.Add(new XComment(substitutionOvenName));
                        }
                        using var newTuningMarkupStream = new MemoryStream();
                        using var tuningMarkupWriter = XmlWriter.Create(newTuningMarkupStream, tuningMarkupWriterSettings);
                        tuningMarkup.Save(tuningMarkupWriter);
                        tuningMarkupWriter.Flush();
                        package.SetResourceContent(recipeTuningKey, newTuningMarkupStream.ToArray());
                        recipesAltered = true;
                    }
                }
                if (recipesAltered)
                {
                    var newPackagePath = Path.Combine(outputDirectory.FullName, packageFile.Name);
                    WriteLine($"  Saving {packageFile.Name}");
                    using var newPackageStream = File.OpenWrite(Path.Combine(outputDirectory.FullName, packageFile.Name));
                    package.SaveTo(newPackageStream);
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded });
            broadcastRecipePackageFile.LinkTo(examineRecipePackageFile, new DataflowLinkOptions { PropagateCompletion = true });
            foreach (var packageFile in inputDirectory.GetFiles("*.package", SearchOption.AllDirectories))
                broadcastRecipePackageFile.Post(packageFile);
            broadcastRecipePackageFile.Complete();
            await examineRecipePackageFile.Completion;
        }
    }

    static TValue QuitIfNull<TValue>(TValue? value)
        where TValue : struct
    {
        if (value is { } nonNullValue)
            return nonNullValue;
        WriteLine("Well, I guess we're not work'n on recipes today.");
        Environment.Exit(0);
        return default;
    }

    static TValue QuitIfNull<TValue>(TValue? obj)
        where TValue : class
    {
        if (obj is { } nonNullObj)
            return nonNullObj;
        WriteLine("Well, I guess we're not work'n on recipes today.");
        Environment.Exit(0);
        return default;
    }

    static DirectoryInfo? SelectDirectory(string prompt)
    {
        while (true)
        {
            WriteLine();
            WriteLine($"{prompt} (enter nothing to exit)");
            Write("Full path: ");
            var path = ReadLine();
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (Directory.Exists(path))
                return new DirectoryInfo(path);
            WriteLine("I don't see a directory there...");
        }
    }

    static ulong? SelectOven(IReadOnlyDictionary<ulong, string> ovens, string prompt)
    {
        while (true)
        {
            WriteLine();
            WriteLine($"{prompt} (enter nothing to exit)");
            WriteLine("Here are the ovens which I know about:");
            foreach (var (id, name) in ovens.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)))
                WriteLine($"    {id}<!--{name}-->");
            Write("Oven ID: ");
            var ovenIdStr = ReadLine();
            if (string.IsNullOrWhiteSpace(ovenIdStr))
                return null;
            if (ulong.TryParse(ovenIdStr, out var ovenId))
            {
                if (ovens.ContainsKey(ovenId))
                    return ovenId;
                WriteLine("Well, that isn't one of the ovens I know about...");
            }
            else
                WriteLine("That's not even a positive integer!");
        }
    }
}
