using System.Diagnostics;
using Userscript2Extension;

try
{
#if !DEBUG
    if (args.Length == 0)
    {
        Helpers.Log("Please call this executable with one argument, that argument being the full path to a valid tampermonkey script (.js file)", LogType.Error);
    }
    else {
        Converter conv = new(args[0]);
        conv.Convert(false, false);
    }
#else
    bool ConvertToChromeExtension = true;
    new Converter(
    PathUserscript: @"C:\Users\Root\Desktop\Programming\Tampermonkey\Example Script\Tampermonkey Script\index.user.js",
    IsChromeExtension: ConvertToChromeExtension)
        .Convert(RunMinifyPass: false,
                 RunPackingPass: false);
#endif
    Helpers.Log($"Press any key to exit the program");
    Console.ReadKey();
} catch (Exception err)
{
    Helpers.Log("Something went wrong, see console output for details!", LogType.Error);
    Console.WriteLine(err.Message);
    Debugger.Break();
    Helpers.Log($"Press any key to exit the program");
    Console.ReadKey();
}
