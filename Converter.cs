using System;
using System.Diagnostics;

namespace Userscript2Extension
{
    internal class Converter
    {
        internal string _UserscriptBaseFolder;
        internal string _UserscriptContent;
        internal string[] _UserscriptContentLines;

        internal Header _UserscriptHeader;

        public Converter(string PathUserscript) {
            _UserscriptBaseFolder = Path.GetDirectoryName(PathUserscript);
            _UserscriptContent = File.ReadAllText(PathUserscript);
            _UserscriptContentLines = _UserscriptContent.Split("\r\n");
        }

        internal Header ParseHeader() {
            const string HeaderStart = "// ==UserScript==";
            const string HeaderEnd = "// ==/UserScript==";

            int SliceStart = Array.FindIndex(_UserscriptContentLines, line => line.Equals(HeaderStart, StringComparison.InvariantCultureIgnoreCase));
            int SliceEnd = Array.FindIndex(_UserscriptContentLines, line => line.Equals(HeaderEnd, StringComparison.InvariantCultureIgnoreCase));

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
            string Buffer = "{\n\t\"manifest_version\": 3,\n";

            string[] SkippableKeys = ["grant", "match", "require"];

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
                        break;
                    default:
                        Console.WriteLine($"Skipping key '{Key}'");
                        break;
                }
            }

            string[] GrantsNeedingPermission = ["GM_setClipboard", "GM_setValue", "GM_getValue", "GM_deleteValue"];
            IEnumerable<string> SpecialGrants = _UserscriptHeader.Headers["grant"].Where(grant => GrantsNeedingPermission.Contains(grant));
            List<string> AddedPermissions = [];
            Buffer += $"\t\"permissions\": [";
            for (int i = 0; i < SpecialGrants.Count(); i++)
            {
                string Grant = SpecialGrants.ElementAt(i);
                string PermissionNeeded = "";

                switch (Grant)
                {
                    case "GM_setClipboard":
                        PermissionNeeded = "clipboardWrite";
                        break;
                    case "GM_setValue":
                    case "GM_getValue":
                    case "GM_deleteValue":
                        PermissionNeeded = "storage";
                        break;
                }

                if (!string.IsNullOrEmpty(PermissionNeeded) && !AddedPermissions.Contains(PermissionNeeded))
                {
                    AddedPermissions.Add(PermissionNeeded);
                    Buffer += $"\"{PermissionNeeded}\",";
                }
            }
            Buffer = Buffer.TrimEnd(',');
            Buffer += "],\n";

            IEnumerable<string> Matches = _UserscriptHeader.Headers["match"];
            Buffer += $"\t\"content_scripts\": [{{\n\t\t\"matches\": [";
            for (int i = 0; i < Matches.Count(); i++)
            {
                string match = Matches.ElementAt(i);
                Buffer += $"\"{match}\",";
            }
            Buffer = Buffer.TrimEnd(',');
            Buffer += "],\n";
            Buffer += "\t\t\"js\": [\"content-script.js\"]\n\t}],\n";
            Buffer += $"\t\"background\": {{ \"service_worker\": \"service_worker.js\" }}";

            ManifestContent = $"{Buffer}\n}}";
            return true;
        }

        internal bool BuildContentScript(out string ContentScriptContent)
        {
            IEnumerable<string> Requires = _UserscriptHeader.Headers["require"];
            IEnumerable<string> Grants = _UserscriptHeader.Headers["grant"];

            // TODO: Stupid hack to make GM_get/set/deleteValue work without
            // having to use the clusterfuck chrome storage api
            string Buffer = $"const keyPrefix = \"{_UserscriptHeader.Headers["name"].First().Replace(" ", "_")}.\";\n";

            foreach (string Grant in Grants)
            {
                Buffer += RewriteGrant(Grant);
            }

            foreach (string Require in Requires) {
                bool IsLocalFile = Require.StartsWith("file:///");
                bool IsRelativePath = IsLocalFile && !Path.IsPathRooted(Require.Split("file:///")[1]);
                string ScriptContent = "";
                if (IsLocalFile)
                {
                    if (IsRelativePath) ScriptContent = File.ReadAllText(Path.Combine(_UserscriptBaseFolder, Require.Split("file:///")[1]));
                    else ScriptContent = File.ReadAllText(Require.Split("file:///")[1]);
                } else
                {
                    ScriptContent = new System.Net.WebClient().DownloadString(Require);
                }
                Buffer += $"{ScriptContent}\n";
            }

            const string HeaderEnd = "// ==/UserScript==";
            int SliceStart = Array.FindIndex(_UserscriptContentLines, line => line.Equals(HeaderEnd, StringComparison.InvariantCultureIgnoreCase));
            Buffer += string.Join("\n", _UserscriptContentLines[(SliceStart + 1)..]);

            if (_UserscriptHeader.Headers.TryGetValue("run-at", out var Values))
            {
                var Value = Values.First();
                Console.WriteLine($"Ignored @run-at directive ({Value})");
                if (!Value.Equals("document-start") 
                    && !Value.Equals("context-menu"))
                {
                    switch (Value)
                    {
                        case "document-idle":
                        case "document-end":
                            // Run when 'DOMContentLoaded' event gets called
                            break;
                        case "document-body":
                            // Run when document.body is not null
                            break;
                    }
                }
            }

            ContentScriptContent = Buffer;
            return true;
        }

        internal bool BuildBackgroundScript(out string BackgroundScriptContent)
        {
            BackgroundScriptContent = "";
            return true;
        }

        internal bool Finalize(string ContentScriptText, string ManifestText, string ServiceWorkerText, out string ExtensionPath)
        {
            string OutputPath = Directory.CreateDirectory(Path.Combine(_UserscriptBaseFolder, _UserscriptHeader.Headers["name"].First())).FullName;
            File.WriteAllText(Path.Combine(OutputPath, "manifest.json"), ManifestText);
            File.WriteAllText(Path.Combine(OutputPath, "content-script.js"), ContentScriptText);
            File.WriteAllText(Path.Combine(OutputPath, "service_worker.js"), ServiceWorkerText);

            // TODO: Automatically pack extension via chrome.exe

            ExtensionPath = OutputPath;
            return true;
        }

        internal readonly Dictionary<string, string> RewriteLookupTable = new()
        {
            { "GM_log", "function GM_log(message) { console.log(message); }\n" },
            { "GM_addElement", 
                "function GM_addElement(...args) {\n" +
                "   const ext = args.length === 3;\n" +
                "   const tagName = args[ext ? 1 : 0];\n" +
                "   const attributes = args[ext ? 1 : 2];\n" +
                "   const parent = ext ? args[0] : null;\n" +
                "   const el = document.createElement(tagName);\n" +
                "   /* handle attributes */\n" +
                "   return ext ? parent.appendChild(el) : document.body.appendChild(el);" +
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
            { "GM_setClipboard",
                "function GM_setClipboard(data, info, cb) {\n" +
                "   debugger;\n" +
                "}\n" },
            { "GM_xmlhttpRequest",
                "function GM_xmlhttpRequest(details) {\n" +
                "   debugger;\n" +
                "}\n" }
        };
        internal string RewriteGrant(string GrantName)
        {
            if (RewriteLookupTable.TryGetValue(GrantName, out string Rewrite)) return Rewrite;
            else
            {
                Console.WriteLine($"[X] Missing implementation for {GrantName}");
                return $"function {GrantName}(args) {{ console.warn(`Implementation for '{GrantName}' has not been written`); }}\n";
            }
        }

        public void Convert() {
            _UserscriptHeader = ParseHeader();

            bool Success = BuildManifest(out string ManifestText);
            Success = BuildContentScript(out string ContentText);
            Success = BuildBackgroundScript(out string BackgroundText);

            Success = Finalize(ContentText, ManifestText, BackgroundText, out string Path);
            Console.WriteLine("Extension data placed at: " + Path);
            Debugger.Break();
        }
    }

    internal class Header
    {
        internal Dictionary<string, IEnumerable<string>> Headers = new();
    }
}
