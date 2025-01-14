# Userscript2Extension
 A very work in process Tampermonkey Script to Chrome Extension converter. If you only have a simple DOM manipulation script this
 program can handle it just fine.

# Supported features (so far)
 * @require - Supports both local files (file:///) and remote urls
 * @grant - See section below to see what functions are supported
 * @match - Should work fully
 * @run-at
 * @noframes
 * @connect (not tested fully, came with re-implementation of GM_xmlhttpRequest)
 * @sandbox
 * @icon (works, just unoptimized/bad way of dealing with it)

# Supported GM_* functions
 * GM_log
 * GM_info (barely)
 * GM_addElement
 * GM_addStyle
 * GM_setValue
 * GM_getValue
 * GM_deleteValue
 * GM_listValues
 * GM_xmlhttpRequest (just uses fetch under the hood, and requires @connect directives to be specified also)
 * unsafeWindow (barely, just returns the first available 'window' instance to the script, unless @sandbox is set to MAIN_WORLD)

# Stuff that arent working/supported
 * Basically everything else thats not mentioned above (which is still a lot, and even for the functions mentioned above, none of the async versions are implemented)

# Stuff that needs to be done (in no particular order)
 * Test implementation of GM_xmlhttpRequest (along with with @connect)
 * A somewhat functional version of GM_info could be nice, where you can atleast get some script information, now it just returns an empty object
 * Actually implement a proper version of unsafeWindow
 * ~~Make chrome extension inherit whatever icon is specified in the tampermonkey script header~~ Tidy up implementation for this
 * Implement auto packing of outputted extension by calling chrome.exe with the --pack-extension commandline flag
 * Eventually get the program to be able to generate firefox extensions also

# How to use
 Open the project, pass an absolute path to the Converter class and then call Convert on it.
 If the program didnt crash, it should have outputted a path where the extension files are stored.
 In chrome, enable Developer mode in the extension window and press Load Unpacked and select the directory
 where the generated extension files are located

# Good to haves
 * ffmpeg in your PATH as its used to automatically resize icon specified by the @icon tampermonkey header, without it (and the provided icon is not 48x48 in size) uses the favicon for tampermonkey.net