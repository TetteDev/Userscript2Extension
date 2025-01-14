using System.Diagnostics;
using System.IO.Compression;
using static Userscript2Extension.Helpers;

namespace Userscript2Extension
{
    internal class Converter
    {
        internal string _UserscriptBaseFolder;
        internal string _UserscriptContent;
        internal string[] _UserscriptContentLines;
        internal Header _UserscriptHeader;
        internal bool _IsChromeExtension;

        internal const string ManifestFileName = "manifest.json";
        internal const string ContentScriptFileName = "content-script.js";
        internal const string ServiceWorkerFileName = "service_worker.js";

        public Converter(string PathUserscript, bool IsChromeExtension = true) {
            _IsChromeExtension = IsChromeExtension;
            _UserscriptBaseFolder = Path.GetDirectoryName(PathUserscript);
            _UserscriptContent = File.ReadAllText(PathUserscript);
            _UserscriptContentLines = _UserscriptContent.Split("\r\n");
        }

        internal Header ParseHeader() {
            //const string HeaderStart = "// ==UserScript==";
            //const string HeaderEnd = "// ==/UserScript==";
            const string HeaderStart = "==UserScript==";
            const string HeaderEnd = "==/UserScript==";

            int SliceStart = Array.FindIndex(_UserscriptContentLines, line => line.Contains(HeaderStart, StringComparison.InvariantCultureIgnoreCase));
            int SliceEnd = Array.FindIndex(_UserscriptContentLines, line => line.Contains(HeaderEnd, StringComparison.InvariantCultureIgnoreCase));

            var Slice = _UserscriptContentLines[(SliceStart+1)..SliceEnd].Select(line => line.TrimStart('/').Trim());
            Header ScriptHeader = new();

            foreach (string line in Slice)
            {
                string HeaderKey = string.Join("", line.TakeWhile(chr => !char.IsWhiteSpace(chr))).Replace("@", string.Empty);
                string HeaderValue = string.Join("", line.Skip($"@{HeaderKey}".Length).SkipWhile(chr => char.IsWhiteSpace(chr)));
                
                if (ScriptHeader.Headers.TryGetValue(HeaderKey, out IEnumerable<string>? value))
                {
                    ScriptHeader.Headers[HeaderKey] = value.Append(HeaderValue);
                } else
                {
                    ScriptHeader.Headers.Add(HeaderKey, [HeaderValue]);
                }
            }

            return ScriptHeader;
        }

        internal bool BuildManifest(out string ManifestContent) {
            Helpers.Log($"Generating {ManifestFileName}");
            string Buffer = "{\n\t\"manifest_version\": 3,\n";

            string[] SkippableKeys = ["grant", "match", "require", "run-at", "connect", "icon", "noframes", "sandbox"];

            foreach (KeyValuePair<string, IEnumerable<string>> kvp in _UserscriptHeader.Headers.Where(kvp => !SkippableKeys.Contains(kvp.Key)))
            {
                string Key = kvp.Key;
                IEnumerable<string> Value = kvp.Value;
                
                switch (Key)
                {
                    case "name":
                    case "version":
                    case "description":
                        Buffer += $"\t\"{Key}\": \"{Value.First()}\",\n";
                        Helpers.Log($"Handled header directive @{Key}", LogType.Success, true);
                        break;
                    default:
                        Helpers.Log($"Skipping header directive @{Key}", LogType.Debug, true);
                        break;
                }
            }

            bool ForcePlaceholderIcon = true;
            if (_UserscriptHeader.Headers.TryGetValue("icon", out IEnumerable<string> Icons))
            {
                string Icon = Icons.First();
                
                bool IsRemoteUrl = Uri.TryCreate(Icon, UriKind.Absolute, out Uri? Result);
                IsRemoteUrl &= Result?.Scheme == Uri.UriSchemeHttp || Result?.Scheme == Uri.UriSchemeHttps;

                if (IsRemoteUrl && !ForcePlaceholderIcon)
                {
                    byte[] ImageBytes = new HttpClient().GetByteArrayAsync(Icon).GetAwaiter().GetResult();
                    string ImageOutputFolder = Directory.CreateDirectory(Path.Combine(_UserscriptBaseFolder, _UserscriptHeader.Headers["name"].First(), "img")).FullName;
                    string Filename = "fetched.png";
                    File.WriteAllBytes(Path.Combine(ImageOutputFolder, Filename), ImageBytes);

                    bool HasFfmpeg = Helpers.TryResolveFfmpegPath(out _);
                    if (HasFfmpeg)
                    {
                        string[] NeededFfmpegScales = ["16:16", "32:32", "48:48", "128:128"];
                        foreach (var scale in NeededFfmpegScales)
                        {
                            string OutputFilename = $"icon{scale.Split(':')[0]}.png";
                            Helpers.ExecuteCommand($"ffmpeg -y -i \"{Path.Combine(ImageOutputFolder, Filename)}\" -pix_fmt rgba -vf scale={scale} \"{Path.Combine(ImageOutputFolder, OutputFilename)}\"");
                        }
                        File.Delete(Path.Combine(ImageOutputFolder, Filename));
                        Buffer += 
                            "\t\"icons\": {\n" +
                                "\t\t\"16\": \"img/icon16.png\",\n" +
                                "\t\t\"32\": \"img/icon32.png\",\n" +
                                "\t\t\"48\": \"img/icon48.png\",\n" +
                                "\t\t\"128\": \"img/icon128.png\"\n" +
                            "\t},\n";
                        Helpers.Log($"Handled header directive @icon (resized image to support all resolutions)", LogType.Warning, true);
                    } else
                    {
                        Size ImageSize = Helpers.GetPngSize(Path.Combine(ImageOutputFolder, Filename));
                        bool FetchedImageCorrectSize = ImageSize.Width == 48 && ImageSize.Height == 48;
                        if (FetchedImageCorrectSize)
                        {
                            File.Move(Path.Combine(ImageOutputFolder, Filename), Path.Combine(ImageOutputFolder, "icon48.png"));
                            Buffer += $"\t\"icons\": {{ \"48\": \"img/icon48.png\" }},\n";
                            Helpers.Log($"Handled header directive @icon (resized image)", LogType.Warning, true);
                        }
                        else
                        {
                            ImageBytes = new HttpClient().GetByteArrayAsync("https://www.google.com/s2/favicons?sz=48&domain=tampermonkey.net").GetAwaiter().GetResult();
                            File.WriteAllBytes(Path.Combine(ImageOutputFolder, "icon48.png"), ImageBytes);
                            File.Delete(Path.Combine(ImageOutputFolder, Filename));
                            Buffer += $"\t\"icons\": {{ \"48\": \"img/{Filename}\" }},\n";
                            Helpers.Log($"Handled header directive @icon (using placeholder)", LogType.Warning, true);
                        }
                        
                    }
                } 
                else
                {
                    byte[] ImageBytes = new HttpClient().GetByteArrayAsync("https://www.google.com/s2/favicons?sz=48&domain=tampermonkey.net").GetAwaiter().GetResult();
                    string ImageOutputFolder = Directory.CreateDirectory(Path.Combine(_UserscriptBaseFolder, _UserscriptHeader.Headers["name"].First(), "img")).FullName;
                    string Filename = "fetched.png";
                    File.WriteAllBytes(Path.Combine(ImageOutputFolder, "icon48.png"), ImageBytes);
                    File.Delete(Path.Combine(ImageOutputFolder, Filename));
                    Buffer += $"\t\"icons\": {{ \"48\": \"img/{Filename}\" }},\n";
                    Helpers.Log($"Handled header directive @icon (using placeholder)", LogType.Warning, true);
                }
            }
            else
            {
                byte[] ImageBytes = new HttpClient().GetByteArrayAsync("https://www.google.com/s2/favicons?sz=48&domain=tampermonkey.net").GetAwaiter().GetResult();
                string ImageOutputFolder = Directory.CreateDirectory(Path.Combine(_UserscriptBaseFolder, _UserscriptHeader.Headers["name"].First(), "img")).FullName;
                string Filename = "fetched.png";
                File.WriteAllBytes(Path.Combine(ImageOutputFolder, "icon48.png"), ImageBytes);
                File.Delete(Path.Combine(ImageOutputFolder, Filename));
                Buffer += $"\t\"icons\": {{ \"48\": \"img/{Filename}\" }},\n";
                Helpers.Log($"Handled header directive @icon (using placeholder)", LogType.Warning, true);
            }

            Dictionary<string, string> GrantPermissionLookup = new()
            {
                { "GM_setClipboard", "clipboardWrite" },
                { "GM_notification", "notifications" },
                { "GM_getTab", "activeTab,tabs" },
            };

            if (_UserscriptHeader.Headers.TryGetValue("grant", out IEnumerable<string> GrantsNeedingPermissions) 
                && (GrantsNeedingPermissions = GrantsNeedingPermissions.Where(grant => GrantPermissionLookup.ContainsKey(grant) && RewriteLookupTable.ContainsKey(grant))).Any())
            {
                List<string> AddedPermissions = [];
                Buffer += $"\t\"permissions\": [";
                for (int i = 0; i < GrantsNeedingPermissions.Count(); i++)
                {
                    string Grant = GrantsNeedingPermissions.ElementAt(i);
                    string PermissionNeeded = GrantPermissionLookup[Grant];

                    if (!string.IsNullOrEmpty(PermissionNeeded))
                    {
                        bool IsMultiplePermissions = PermissionNeeded.Contains(',');
                        if (IsMultiplePermissions)
                        {
                            foreach (string Permission in PermissionNeeded.Split(","))
                            {
                                if (!AddedPermissions.Contains(Permission))
                                {
                                    AddedPermissions.Add(Permission);
                                    Buffer += $"\"{Permission}\",";
                                    Helpers.Log($"Added permission '{Permission}' for {Grant} to permissions", LogType.Success, true);
                                }
                            }

                        }
                        else
                        {
                            if (!AddedPermissions.Contains(PermissionNeeded))
                            {
                                AddedPermissions.Add(PermissionNeeded);
                                Buffer += $"\"{PermissionNeeded}\",";
                                Helpers.Log($"Added permission '{PermissionNeeded}' for {Grant} to permissions", LogType.Success, true);
                            }
                        }
                    }
                }
                Buffer = Buffer.TrimEnd(',');
                Buffer += "],\n";
            }

            if (_UserscriptHeader.Headers.TryGetValue("connect", out IEnumerable<string> Connects))
            {
                Buffer += $"\t\"host_permissions\": [";
                bool HasAllowAllUrlsPermission = Connects.Any(connect => connect.Equals("*"));
                if (HasAllowAllUrlsPermission)
                {
                    Buffer += "\"<all_urls>\"],";
                    Helpers.Log("Added @connect match all directive '*' to host_permissions", LogType.Success, true);
                } else
                {
                    for (int i = 0; i < Connects.Count(); i++)
                    {
                        string Connect = Connects.ElementAt(i);
                        Helpers.Log($"Added @connect directive '{Connect}' to host_permissions", LogType.Success, true);
                        Buffer += $"\"{Connect}\",";
                    }
                    Buffer = Buffer.TrimEnd(',');
                    Buffer += "],\n";
                }
            }

            if (_UserscriptHeader.Headers.TryGetValue("match", out IEnumerable<string> Matches))
            {
                Buffer += $"\t\"content_scripts\": [{{\n\t\t\"matches\": [";
                for (int i = 0; i < Matches.Count(); i++)
                {
                    string match = Matches.ElementAt(i);
                    Buffer += $"\"{match}\",";
                }
                Buffer = Buffer.TrimEnd(',');
                Buffer += "],\n";
            }

            bool HasRunAt = _UserscriptHeader.Headers.ContainsKey("run-at");
            if (HasRunAt)
            {
                string Value = _UserscriptHeader.Headers["run-at"].First();
                Buffer += $"\t\t\"run_at\": \"{Value.Replace("-", "_")}\",\n";
                Helpers.Log($"Handled header directive @run-at", LogType.Success, true);
            } 
            else
            {
                Buffer += $"\t\t\"run_at\": \"document_start\",\n";
            }

            bool HasNoIframes = _UserscriptHeader.Headers.ContainsKey("noframes");
            if (HasNoIframes)
            {
                Buffer += $"\t\t\"all_frames\": false,\n";
                Helpers.Log($"Handled header directive @noframes", LogType.Success, true);
            } 
            else
            {
                Buffer += $"\t\t\"all_frames\": false,\n";
            }

            bool HasSandbox = _UserscriptHeader.Headers.ContainsKey("sandbox");
            if (HasSandbox)
            {
                string Value = _UserscriptHeader.Headers["sandbox"].First();
                string ManifestEquivalent = Value switch
                {
                    "raw" or "JavaScript" => "MAIN",
                    _ => "ISOLATED",
                };
                Buffer += $"\t\t\"world\": \"{ManifestEquivalent}\",\n";
                Helpers.Log($"Handled header directive @sandbox", LogType.Success, true);
            } 
            else
            {
                Buffer += $"\t\t\"world\": \"MAIN\",\n";
            }
            
            Buffer += $"\t\t\"js\": [\"{ContentScriptFileName}\"]\n\t}}],\n";
            Buffer += $"\t\"background\": {{ \"service_worker\": \"{ServiceWorkerFileName}\" }}";

            ManifestContent = $"{Buffer}\n}}";
            return true;
        }

        internal bool BuildContentScript(out string ContentScriptContent)
        {
            Helpers.Log($"Generating {ContentScriptFileName}");

            // TODO: Stupid hack to make GM_get/set/list/deleteValue(s) work without
            // having to use the clusterfuck chrome storage api
            string Buffer = $"const keyPrefix = \"{_UserscriptHeader.Headers["name"].First().Replace(" ", "_").ToLowerInvariant()}.\";\n";

            if (_UserscriptHeader.Headers.TryGetValue("grant", out IEnumerable<string> Grants))
            {
                foreach (string Grant in Grants)
                {
                    Buffer += HandleGrant(Grant, ThrowOnUnhandledGrant: false);
                }
            }
            
            if (_UserscriptHeader.Headers.TryGetValue("require", out IEnumerable<string> Requires))
            {
                foreach (string Require in Requires)
                {
                    bool IsLocalFile = Require.StartsWith("file:///");
                    bool IsRelativePath = IsLocalFile && !Path.IsPathRooted(Require.Split("file:///")[1]);
                    string ScriptContent = "";
                    if (IsLocalFile)
                    {
                        if (IsRelativePath) ScriptContent = File.ReadAllText(Path.Combine(_UserscriptBaseFolder, Require.Split("file:///")[1]));
                        else ScriptContent = File.ReadAllText(Require.Split("file:///")[1]);
                    }
                    else
                    {
                        try
                        {
                            ScriptContent = new HttpClient().GetStringAsync(Require).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            Helpers.Log($"[X] Failed fetching require content from url '{Require}', manual intervention needed!", LogType.Error, true);
                            Console.ReadKey();
                            throw; // Rethrow exception to halt the conversion progress
                        }
                    }

                    Helpers.Log($"Resolved require '{Require}'", LogType.Success, true);
                    Buffer += $"{ScriptContent}\n";
                }
            }

            const string HeaderEnd = "==/UserScript==";
            int SliceStart = Array.FindIndex(_UserscriptContentLines, line => line.Contains(HeaderEnd, StringComparison.InvariantCultureIgnoreCase));
            Buffer += string.Join("\n", _UserscriptContentLines[(SliceStart + 1)..]);

            ContentScriptContent = Buffer;
            return true;
        }

        internal bool BuildBackgroundScript(out string BackgroundScriptContent)
        {
            Helpers.Log($"Generating {ServiceWorkerFileName}");
            string Buffer = "";

            if (_IsChromeExtension)
            {
                Buffer =
                    @"chrome.runtime.onMessage.addListener(function(message, callback) {
                        // Do something with message and callback here
                    });";
                Helpers.Log($"Added placeholder message handler (chrome)", LogType.Debug, true);
            }
            else
            {
                Buffer =
                    @"browser.runtime.onMessage.addListener((data, sender) => {
                        // Do something with data and sender here
                    });";
                Helpers.Log($"Added placeholder message handler (firefox)", LogType.Debug, true);
            }

            BackgroundScriptContent = Buffer;
            return true;
        }

        internal bool Finalize(string ContentScriptText, string ManifestText, string ServiceWorkerText, out string ExtensionPath)
        {
            Helpers.Log($"Writing extension files to disk ...");

            string OutputPath = Directory.CreateDirectory(Path.Combine(_UserscriptBaseFolder, _UserscriptHeader.Headers["name"].First())).FullName;
            File.WriteAllText(Path.Combine(OutputPath, ManifestFileName), ManifestText);
            Helpers.Log($"Wrote {ManifestFileName} to disk", LogType.Success, true);

            File.WriteAllText(Path.Combine(OutputPath, ContentScriptFileName), ContentScriptText);
            Helpers.Log($"Wrote {ContentScriptFileName} to disk", LogType.Success, true);

            File.WriteAllText(Path.Combine(OutputPath, ServiceWorkerFileName), ServiceWorkerText);
            Helpers.Log($"Wrote {ServiceWorkerFileName} to disk", LogType.Success, true);

            // Do packing in here instead of inside Convert

            ExtensionPath = OutputPath;
            return true;
        }


        internal readonly Dictionary<string, string> RewriteLookupTable = new()
        {
            { "GM_log", 
                "function GM_log(message) { console.log(message); }\n" },
            { "GM_addElement", 
                "function GM_addElement(...args) {\n" +
                "   const ext = args.length === 3;\n" +
                "   const tagName = args[ext ? 1 : 0];\n" +
                "   const attributes = args[ext ? 1 : 2];\n" +
                "   const parent = ext ? args[0] : null;\n" +
                "   const el = document.createElement(tagName);\n" +
                "   /* handle attributes */\n" +
                "   return ext ? parent.appendChild(el) : document.body.appendChild(el);\n" +
                "}\n" },
            { "GM_addStyle", 
                "function GM_addStyle(css) {\n" +
                "   const el = document.createElement('style');\n" +
                "   el.type = 'text/css';\n" +
                "   el.appendChild(document.createTextNode(css));\n" +
                "   return (document.head || document.querySelector('head')).appendChild(el);\n" +
                "}\n" },
            { "GM_setValue", 
                "function GM_setValue(key, value) {\n" +
                "   window.localStorage.setItem(keyPrefix + key, value);\n" +
                "}\n" },
            { "GM_getValue",
                "function GM_getValue(key, defValue) {\n" +
                "   let retval = window.localStorage.getItem(keyPrefix + key);\n" +
                "   return (retval || defValue);\n" +
                "}\n" },
            { "GM_deleteValue",
                "function GM_deleteValue(key) {\n" +
                "   window.localStorage.removeItem(keyPrefix + key);\n" +
                "}\n" },
            { "GM_listValues",
                "function GM_listValues() {\n" +
                "   var list = [];\n" +
                "   var reKey = new RegExp(\"^\" + keyPrefix);\n" +
                "   for (var i = 0, il = window.localStorage.length; i < il; i++) {\n" +
                "       var key = window.localStorage.key(i);\n" +
                "       if (key.match(reKey)) {\n" +
                "           list.push(key.replace(keyPrefix, ''));\n" +
                "       }\n" +
                "   }\n" +
                "   return list;\n" +
                "}\n" },
            { "GM_info",
                "Object.defineProperty(window, 'GM_info', { value: {}, writable: true });\n" },
            { "unsafeWindow",
                "Object.defineProperty(window, 'unsafeWindow', { value: window, writable: true });\n" },
            { "GM_xmlhttpRequest",
                "function GM_xmlhttpRequest(details) {\n" +
                "   return new Promise((resolve, reject) => {\n" +
                "       try {\n" +
                "           fetch((details?.url ?? details /* details is probably a string with an url here */), { method: (details?.method ?? 'get') }).then(response => {\n" +
                "               return resolve(response);\n" +
                "           });\n" +
                "       }\n" +
                "       catch (error) {\n" +
                "           return reject(error);\n" +
                "       }\n" +
                "   });\n" +
                "}\n" },

            //{ "GM_setClipboard",
            //    "function GM_setClipboard(data, info, cb) {\n" +
            //    "   debugger;\n" +
            //    "}\n" },

            //{ "GM_getTab",
            //    "function GM_getTab(callback) {\n" +
            //    "   let queryOptions = { active: true, lastFocusedWindow: true };\n" +
            //    "   chrome.tabs.query(queryOptions, ([tab]) => { \n" +
            //    "       callback(tab);\n" +
            //    "   });" +
            //    "}\n"}

            //{ "GM_notification",
            //    "function GM_notification(...args) {\n" +
            //    "   \n" +
            //    "}\n" }
        };
        internal string HandleGrant(string GrantName, bool ThrowOnUnhandledGrant = true)
        {
            if (RewriteLookupTable.TryGetValue(GrantName, out string Rewrite))
            {
                Helpers.Log($"Grant '{GrantName}' handled", LogType.Success, true);
                return Rewrite;
            }
            else
            {
                if (ThrowOnUnhandledGrant)
                {
                    Helpers.Log($"Grant '{GrantName}' not handled, halting execution and {nameof(ThrowOnUnhandledGrant)} was {(ThrowOnUnhandledGrant ? "true" : "false")}, halding execution!", LogType.Error, true);
                    throw new NotImplementedException($"Grant '{GrantName}' not handled, halting execution!");
                } else
                {
                    Helpers.Log($"Missing implementation for {GrantName}, using throwing placeholder ...", LogType.Warning, true);
                    return $"function {GrantName}(...args) {{ console.warn(`Implementation for '{GrantName}' has not been written`); debugger; throw new Error('Function is missing implementation, cannot continue!'); }}\n";
                }
            }
        }
        
        public void Convert(bool RunMinifyPass = false, bool RunPackingPass = false) {
            Helpers.Log($"Attempting to generate a {(_IsChromeExtension ? "chrome" : "firefox")} extension, please wait ...");
            _UserscriptHeader = ParseHeader();

            bool Success = BuildManifest(out string ManifestText);
            Success = BuildContentScript(out string ContentText);
            Success = BuildBackgroundScript(out string BackgroundText);
            Success = Finalize(ContentText, ManifestText, BackgroundText, out string OutputExtensionPath);

            if (RunPackingPass)
            {
                PackExtension(OutputExtensionPath, out OutputExtensionPath);
            }
            
            Helpers.Log("Extension data placed at: " + OutputExtensionPath, LogType.Success);
        }

        internal void PackExtension(string ExtensionPath, out string PackedExtensionPath)
        {
            const bool OverrideDisablePacking = true;
            if (OverrideDisablePacking)
            {
                Helpers.Log($"PackExtension ({(_IsChromeExtension ? "chrome" : "firefox")}) was called but no logic implemented or function was instructed to not pack (ForceDisablePacking was true), ignoring ...", LogType.Warning);
                PackedExtensionPath = ExtensionPath;
                return;
            }

            if (_IsChromeExtension)
            {
                bool Found = Helpers.TryResolveChromePath(out string ChromeExecutablePath);
                if (Found)
                {
                    string ExtensionPrivateKeyPath = Path.Combine(Path.GetDirectoryName(ExtensionPath), _UserscriptHeader.Headers["name"].First());
                    string CommandLine = $@"--pack-extension=""{ExtensionPath}"" --pack-extension-key=""{ExtensionPrivateKeyPath}.pem""";
                    string PackedExtensionOutputPath = Path.Combine(Path.GetDirectoryName(ExtensionPath), Path.Combine(Path.GetDirectoryName(ExtensionPath), _UserscriptHeader.Headers["name"].First()) + ".crx");
                    // Call executable pointed to by path in ChromeExecutablePath with CommandLine as the argument
                    //PackedExtensionPath = PackedExtensionOutputPath;
                }
            }
            else
            {
                string PackedExtensionOutputPath = Path.Combine(Path.GetDirectoryName(ExtensionPath), _UserscriptHeader.Headers["name"].First()) + ".zip";
                ZipFile.CreateFromDirectory(ExtensionPath, PackedExtensionOutputPath);
                //PackedExtensionPath = PackedExtensionOutputPath;
            }

            PackedExtensionPath = ExtensionPath;
        }
    }
}
