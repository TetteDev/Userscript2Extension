bool ConvertToChromeExtension = true;

new Userscript2Extension.Converter(
    PathUserscript: @"C:\Users\Root\Desktop\Programming\Tampermonkey\Example Script\Tampermonkey Script\index.user.js", 
    IsChromeExtension: ConvertToChromeExtension).Convert();

Console.WriteLine($"{Environment.NewLine}Press any key to exit the program");
Console.ReadKey();