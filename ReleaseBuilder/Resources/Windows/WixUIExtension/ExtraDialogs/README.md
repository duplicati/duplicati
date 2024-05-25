# Files from UI.wixext

The files in this folder are adapted from [UI.wixext](https://github.com/wixtoolset/UI.wixext/tree/master/src/wixlib).
The adaptation is mostly to remove the use of localization (`!(loc.name)`) which is not supported by Wixl.
The actual strings are copied from `en-US` and inserted into `CommonExtra.wxs`.

Some modification was required to avoid `"Error 2834: The next pointers on the dialog BrowseDlg do not form a single loop"`.

The files are licensed under the [MS-RL](https://opensource.org/licenses/ms-rl) license.
