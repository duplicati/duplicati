#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    /// <summary>
    /// This class generates a list of source folders and matching filters, based on the users choices
    /// </summary>
    public static class DynamicSetupHelper
    {
        /// <summary>
        /// Gets a source folder setup, based on the users filter selection
        /// </summary>
        /// <param name="wrapper">The wrapper instance with the user selection</param>
        /// <param name="filters">The filter settings</param>
        /// <returns>The source folder string</returns>
        public static string[] GetSourceFolders(Wizard_pages.WizardSettingsWrapper wrapper, ApplicationSettings settings, List<KeyValuePair<bool, string>> filters)
        {
            if (wrapper.SelectFilesUI.Version >= 2 && wrapper.SelectFilesUI.UseSimpleMode)
            {
                return GetSourceFolders(
                    wrapper.SelectFilesUI.IncludeDocuments,
                    wrapper.SelectFilesUI.IncludeMusic,
                    wrapper.SelectFilesUI.IncludeImages,
                    wrapper.SelectFilesUI.IncludeDesktop,
                    wrapper.SelectFilesUI.IncludeSettings,
                    settings,
                    filters);
            }
            else
            {
                return PrependFilterList(wrapper.SourcePath.Split(System.IO.Path.PathSeparator), settings, filters);
            }
        }

        /// <summary>
        /// Gets a source folder setup, based on the users filter selection
        /// </summary>
        /// <param name="task">The Task instance with the user setup</param>
        /// <param name="filters">The filter settings</param>
        /// <returns>The source folder string</returns>
        public static string[] GetSourceFolders(Task task, ApplicationSettings settings, List<KeyValuePair<bool, string>> filters)
        {
            if (task.Extensions.SelectFiles_Version >= 2 && task.Extensions.SelectFiles_UseSimpleMode)
            {
                return GetSourceFolders(
                    task.Extensions.SelectFiles_IncludeDocuments,
                    task.Extensions.SelectFiles_IncludeMusic,
                    task.Extensions.SelectFiles_IncludeImages,
                    task.Extensions.SelectFiles_IncludeDesktop,
                    task.Extensions.SelectFiles_IncludeAppData,
                    settings,
                    filters);
            }
            else
            {
                return PrependFilterList(task.SourcePath.Split(System.IO.Path.PathSeparator), settings, filters);
            }
        }

        /// <summary>
        /// Prepends extra filters to exclude the temp folder and the Duplicati signature cache
        /// </summary>
        /// <param name="sourceFolders">The list of folders being backed up</param>
        /// <param name="filters">The current set of filters to prepend to</param>
        /// <returns>The sourceFolders</returns>
        private static string[] PrependFilterList(string[] sourceFolders, ApplicationSettings settings, List<KeyValuePair<bool, string>> filters)
        {
            string[] exFolders = new string[] {
                Library.Utility.Utility.AppendDirSeparator(System.Environment.ExpandEnvironmentVariables(settings.SignatureCachePath)), 
                Library.Utility.Utility.AppendDirSeparator(System.Environment.ExpandEnvironmentVariables(settings.TempPath))
            };

            foreach (string i in sourceFolders)
                foreach (string x in exFolders)
                    if (x.StartsWith(i, Library.Utility.Utility.ClientFilenameStringComparision))
                        filters.Insert(0, new KeyValuePair<bool, string>(false, Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(x)));

            return sourceFolders;
        }

        /// <summary>
        /// Gets a source folder setup, based on the users folder selection
        /// </summary>
        /// <param name="filters">The filter settings</param>
        /// <param name="includeDocuments">True if documents should be included</param>
        /// <param name="includeMusic">True if music should be included</param>
        /// <param name="includeImages">True if images should be included</param>
        /// <param name="includeDesktop">True if desktop files should be included</param>
        /// <param name="includeSettings">True if settings should be included</param>
        /// <returns>The source folder string</returns>
        private static string[] GetSourceFolders(bool includeDocuments, bool includeMusic, bool includeImages, bool includeDesktop, bool includeSettings, ApplicationSettings settings, List<KeyValuePair<bool, string>> filters)
        {
            string myPictures = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            string myMusic = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            string desktop = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            string appData = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            string myDocuments = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            List<string> folders = new List<string>();
            List<string> exfolders = new List<string>();
            
            (includeDocuments ? folders : exfolders).Add(myDocuments);
            (includeImages ? folders : exfolders).Add(myPictures);
            (includeMusic ? folders : exfolders).Add(myMusic);
            (includeDesktop ? folders : exfolders).Add(desktop);
            (includeSettings ? folders : exfolders).Add(appData);

            if (folders.Count == 0)
                throw new Exception(Strings.DynamicSetupHelper.NoFoldersInSetupError);

            //Figure out if any folders are subfolders, and only include the parents
            for (int i = 0; i < folders.Count; i++)
                for (int j = i + 1; j < folders.Count; j++)
                    if (folders[i].StartsWith(folders[j], Library.Utility.Utility.ClientFilenameStringComparision))
                    {
                        folders.RemoveAt(i);
                        i--;
                        break; //Break inner, continue outer
                    }
                    else if (folders[j].StartsWith(folders[i], Library.Utility.Utility.ClientFilenameStringComparision))
                    {
                        folders.RemoveAt(j);
                        i = -1; //Restart loop
                        break; //Break inner, continue outer
                    }

            //Add filters to exclude de-selected folders
            foreach (string s in exfolders)
            {
                foreach (string s2 in folders)
                    if (s.StartsWith(s2))
                    {
                        filters.Insert(0, new KeyValuePair<bool, string>(false, Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(s)));
                        break;
                    }
            }

            string fi = "";
            foreach (KeyValuePair<bool, string> x in filters)
                fi += x.Value + ";";

            return PrependFilterList(folders.ToArray(), settings, filters);
        }
    }
}
