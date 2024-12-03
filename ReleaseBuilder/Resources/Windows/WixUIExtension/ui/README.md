# WIX UI custom files

The base contents of this folder are copied from the [Wixl UI extensions](https://gitlab.gnome.org/GNOME/msitools/-/tree/master/data/ext/ui).
The [`Common.wxs`](./Common.wxs) file has been modified to use the custom banners.
This was needed as it is not possible to pass outside strings/resources into the project as it is.

Some extra files are included from [UI.wixext](https://github.com/wixtoolset/UI.wixext/tree/master/src/wixlib).
The adaptation is mostly to remove the use of localization (`!(loc.name)`) which is not supported by Wixl. The actual strings are copied from `en-US` and inserted into `Common.wxs`.

Also, the dialogs were modified to place the buttons as the first items in the dialog, because it otherwise causes the error: `"Error 2834: The next pointers on the dialog BrowseDlg do not form a single loop"`.

The extra files are:

- [`BrowseDlg.wxs`](./BrowseDlg.wxs)
- [`CustomizeDlg.wxs`](./CustomizeDlg.wxs)
- [`DiskCostDlg.wxs`](./DiskCostDlg.wxs)
- [`LicenseAgreementDlg.wxs`](./LicenseAgreementDlg.wxs)
- [`WixUI_FeatureTree.wxs`](./WixUI_FeatureTree.wxs)
- [`WixUI_InstallDir.wxs`](./WixUI_InstallDir.wxs)
- [`InstallDirDlg.wxs`](./InstallDirDlg.wxs)
- [`InvalidDirDlg.wxs`](./InvalidDirDlg.wxs)

The files are all licensed under the [MS-RL](https://opensource.org/licenses/ms-rl) license.
