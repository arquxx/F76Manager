﻿using Fo76ini.Ini;
using Fo76ini.Utilities;
using IniParser;
using IniParser.Model;
using IniParser.Model.Configuration;
using IniParser.Parser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fo76ini
{
    public class IniFile
    {
        public readonly string FilePath;
        public readonly string FileName;

        /// <summary>
        /// Fallback to this path, if the actual path doesn't exist. (To load defaults)
        /// </summary>
        public string DefaultPath;

        public bool IsReadOnly
        {
            get
            {
                if (File.Exists(FilePath))
                    return new FileInfo(FilePath).IsReadOnly;
                else
                    return false;
            }
            set
            {
                SetFileReadOnlyAttribute(value);
            }
        }

        private FileIniDataParser iniParser;
        private IniData data;

        private DateTime lastModified;

        // *.ini files used by the engine are encoded as UTF-8 without BOM
        private Encoding encoding = new UTF8Encoding(false);

        // Comments start with ";" or "#" and can be inline
        // Previous regex (@"^;.*") only permitted comments delimited with ";" at the beginning of a line
        // Since the s76UserName and s76Password tweaks are useless now, I hope nobody will complain if the tool doesn't support passwords with ";" or "#" in them
        public static Regex CommentRegex = new Regex(@"[;#].*");

        public IniFile(String path, String defaultPath = null)
        {
            this.FilePath = path;
            this.FileName = Path.GetFileName(path);
            this.DefaultPath = defaultPath;

            // Configuring INI parser
            IniParserConfiguration iniParserConfig = new IniParserConfiguration();
            iniParserConfig.AllowCreateSectionsOnFly = true;
            iniParserConfig.AssigmentSpacer = "";
            iniParserConfig.CaseInsensitive = true;
            iniParserConfig.CommentRegex = CommentRegex;

            // Be very generous, allow everything:
            iniParserConfig.AllowDuplicateKeys = true;
            iniParserConfig.AllowDuplicateSections = true;
            iniParserConfig.AllowKeysWithoutSection = true;
            iniParserConfig.OverrideDuplicateKeys = true;

            // Initialize INI parser
            this.iniParser = new FileIniDataParser(new IniDataParser(iniParserConfig));
        }

        public void Save()
        {
            if (data == null)
                return;

            RemoveEmptySections();
            bool readOnly = this.IsReadOnly;
            SetFileReadOnlyAttribute(false);
            this.iniParser.WriteFile(FilePath, data, encoding);
            SetFileReadOnlyAttribute(readOnly);
            UpdateLastModifiedDate();
        }

        /// <summary>
        /// Loads the *.ini file if it exists.
        /// If not, try to load the defaults from DefaultPath.
        /// If DefaultPath isn't specified or the default file doesn't exist, then create a new file.
        /// Raises Fo76ini.Ini.IniParsingException unless ignoreErrors has been set to true.
        /// </summary>
        /// <param name="ignoreErrors">Set this to true to ignore any errors and create an empty file instead.</param>
        public void Load(bool ignoreErrors = false)
        {
            try
            {
                if (File.Exists(FilePath))
                    data = this.iniParser.ReadFile(FilePath, encoding);
                else if (DefaultPath != null && File.Exists(DefaultPath))
                    data = this.iniParser.ReadFile(DefaultPath, encoding);
                else
                    data = new IniData();
            }
            catch (IniParser.Exceptions.ParsingException exc)
            {
                // Add the path to the exception (since IniParser doesn't do this) and throw again:
                // throw new IniParser.Exceptions.ParsingException($"{Path} couldn't be parsed: {e.Message}", e);
                if (!ignoreErrors)
                    throw IniParsingException.CreateException(exc, FilePath);
                data = new IniData();
            }
            UpdateLastModifiedDate();
        }

        public void LoadDefault()
        {
            try
            {
                if (DefaultPath != null && File.Exists(DefaultPath))
                    data = this.iniParser.ReadFile(DefaultPath, encoding);
                else
                    data = new IniData();
            }
            catch
            {
                data = new IniData();
            }
            UpdateLastModifiedDate();
        }

        public bool IsLoaded()
        {
            return data != null;
        }

        /// <summary>
        /// Merges the other *.ini file into this one by overwriting existing values. Comments get appended.
        /// </summary>
        /// <param name="f"></param>
        public void Merge(IniFile f)
        {
            this.data.Merge(f.data);
        }


        /// <summary>
        /// Merges the other iniData into this one by overwriting existing values. Comments get appended.
        /// </summary>
        /// <param name="f"></param>
        public void Merge(IniData d)
        {
            this.data.Merge(d);
        }

        /// <summary>
        /// Check whether the file has been modified outside of the tool.
        /// </summary>
        /// <returns></returns>
        public bool FileHasBeenModified()
        {
            return this.lastModified != File.GetLastWriteTime(FilePath);
        }

        public void UpdateLastModifiedDate()
        {
            this.lastModified = File.GetLastWriteTime(FilePath);
        }

        protected void RemoveEmptySections()
        {
            List<string> sectionNames = new List<string>();
            foreach (SectionData section in data.Sections)
                if (section.Keys.Count == 0)
                    sectionNames.Add(section.SectionName);
            foreach (string sectionName in sectionNames)
                data.Sections.RemoveSection(sectionName);
        }

        public void ClearAllComments()
        {
            this.data.ClearAllComments();
        }

        protected void SetFileReadOnlyAttribute(bool readOnly)
        {
            // https://stackoverflow.com/questions/8081242/c-sharp-make-file-read-write-from-readonly
            if (File.Exists(FilePath))
            {
                var attr = File.GetAttributes(FilePath);

                if (readOnly)
                    attr = attr | FileAttributes.ReadOnly;
                else
                    attr = attr & ~FileAttributes.ReadOnly;

                File.SetAttributes(FilePath, attr);
            }
        }

        /*
         *********************************************************************************************************************************************
         * GETTER AND SETTER
         ********************************************************************************************************************************************
         */

        public bool Exists(string section, string key)
        {
            return data != null && data[section][key] != null;
        }

        public string GetString(string section, string key, string defaultValue)
        {
            return Exists(section, key) ? data[section][key] : defaultValue;
        }

        /// <summary>
        /// Raises KeyNotFoundException if [section]key couldn't be found. See GetString(section, key, defaultValue)
        /// </summary>
        public string GetString(string section, string key)
        {
            if (!Exists(section, key))
                throw new KeyNotFoundException($"Couldn't find [{section}] {key} in any *.ini file.");
            return data[section][key];
        }

        public static readonly string[] ValidBoolValues = new string[] { "1", "0", "" };

        /// <summary>
        /// Raises KeyNotFoundException if [section]key couldn't be found. See GetBool(section, key, defaultValue)
        /// </summary>
        public bool GetBool(string section, string key)
        {
            return GetString(section, key) == "1";
        }

        public bool GetBool(string section, string key, bool defaultValue)
        {
            string value = GetString(section, key, defaultValue ? "1" : "0");
            if (ValidBoolValues.Contains(value))
                return value == "1";
            else
                return defaultValue;
        }

        /// <summary>
        /// Raises KeyNotFoundException if [section]key couldn't be found. See GetFloat(section, key, defaultValue)
        /// </summary>
        public float GetFloat(string section, string key)
        {
            return Utils.ToFloat(GetString(section, key));
        }

        public float GetFloat(string section, string key, float defaultValue)
        {
            try
            {
                return Utils.ToFloat(GetString(section, key, defaultValue.ToString(Shared.en_US)));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Raises KeyNotFoundException if [section]key couldn't be found. See GetUInt(section, key, defaultValue)
        /// </summary>
        public uint GetUInt(string section, string key)
        {
            return Utils.ToUInt(GetString(section, key));
        }

        public uint GetUInt(string section, string key, uint defaultValue)
        {
            try
            {
                return Utils.ToUInt(GetString(section, key, defaultValue.ToString(Shared.en_US)));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Raises KeyNotFoundException if [section]key couldn't be found. See GetInt(section, key, defaultValue)
        /// </summary>
        public int GetInt(string section, string key)
        {
            return Utils.ToInt(GetString(section, key));
        }

        public int GetInt(string section, string key, int defaultValue)
        {
            try
            {
                return Utils.ToInt(GetString(section, key, defaultValue.ToString(Shared.en_US)));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Raises KeyNotFoundException if [section]key couldn't be found. See GetLong(section, key, defaultValue)
        /// </summary>
        public long GetLong(string section, string key)
        {
            return Utils.ToLong(GetString(section, key));
        }

        public long GetLong(string section, string key, int defaultValue)
        {
            try
            {
                return Utils.ToLong(GetString(section, key, defaultValue.ToString(Shared.en_US)));
            }
            catch
            {
                return defaultValue;
            }
        }

        public void Set(string section, string key, string value)
        {
            try
            {
                data[section][key] = value;
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't assign the key [{section}] {key} to {value}.");
            }
        }

        public void Set(string section, string key, int value)
        {
            try
            {
                Set(section, key, Utils.ToString(value));
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't set the key [{section}] {key} to {Utils.ToString(value)}.");
            }
        }

        public void Set(string section, string key, uint value)
        {
            try
            {
                Set(section, key, Utils.ToString(value));
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't set the key [{section}] {key} to {Utils.ToString(value)}.");
            }
        }

        public void Set(string section, string key, long value)
        {
            try
            {
                Set(section, key, Utils.ToString(value));
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't set the key [{section}] {key} to {Utils.ToString(value)}.");
            }
        }

        public void Set(string section, string key, float value)
        {
            try
            {
                Set(section, key, Utils.ToString(value));
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't set the key [{section}] {key} to {Utils.ToString(value)}.");
            }
        }

        public void Set(string section, string key, double value)
        {
            try
            {
                Set(section, key, Utils.ToString(value));
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't set the key [{section}] {key} to {Utils.ToString(value)}.");
            }
        }

        public void Set(string section, string key, bool value)
        {
            try
            {
                Set(section, key, value ? "1" : "0");
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't set the key [{section}] {key} to {Utils.ToString(value)}.");
            }
        }

        public void Remove(string section, string key)
        {
            try
            {
                if (Exists(section, key))
                    data[section].RemoveKey(key);
            }
            catch
            {
                throw new KeyNotFoundException($"Couldn't find [{section}] {key} in INI file.");
            }
        }
    }
}
