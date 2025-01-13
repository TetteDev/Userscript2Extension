using System.Diagnostics;
using Userscript2Extension;

bool ConvertToChromeExtension = true;

try
{
    new Converter(
    PathUserscript: @"C:\Users\Root\Desktop\Programming\Tampermonkey\Example Script\Tampermonkey Script\index.user.js",
    IsChromeExtension: ConvertToChromeExtension)
        .Convert(RunMinifyPass: false,
                 RunPackingPass: false);

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
