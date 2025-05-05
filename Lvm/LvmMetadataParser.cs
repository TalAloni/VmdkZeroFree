/* Copyright (C) 2025 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System.Collections.Generic;
using System.Text;

namespace VmdkZeroFree.Lvm
{
    public static class LvmMetadataParser
    {
        public static string GetMetadataObject(string metadata, string objectName)
        {
            string[] lines = metadata.Split('\n');
            int depth = 0;
            StringBuilder builder = new StringBuilder();
            int? objectDepth = null;
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (line.Contains('{'))
                {
                    if (line == $"{objectName} {{")
                    {
                        objectDepth = depth;
                    }
                    depth++;
                }

                if (objectDepth.HasValue)
                {
                    builder.Append(line);
                    builder.Append('\n');
                }

                if (line.Contains('}'))
                {
                    depth--;
                    if (objectDepth.HasValue && depth == objectDepth.Value)
                    {
                        return builder.ToString();
                    }
                }
            }

            return null;
        }

        public static List<string> GetChildObjects(string metadata)
        {
            string[] lines = metadata.Split('\n');
            int depth = 0;
            List<string> result = new List<string>();
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int indexOfObjectStart = line.IndexOf('{');
                if (indexOfObjectStart >= 0)
                {
                    if (depth == 1)
                    {
                        string objectName = line.Substring(0, indexOfObjectStart - 1);
                        result.Add(objectName);
                    }
                    depth++;
                }
                else if (line.Contains('}'))
                {
                    depth--;
                }
            }

            return result;
        }

        public static string GetValue(string metadata, string keyName)
        {
            string[] lines = metadata.Split('\n');
            int depth = 0;
            List<string> result = new List<string>();
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                if (line.Contains('{'))
                {
                    depth++;
                }
                else if (line.Contains('}'))
                {
                    depth--;
                }

                if (line.StartsWith($"{keyName} = "))
                {
                    string value = line.Substring(keyName.Length + 3);
                    if (value.StartsWith('['))
                    {
                        while (!value.EndsWith(']'))
                        {
                            value += lines[index + 1];
                            index++;
                        }
                    }
                    return value;
                }
            }

            return null;
        }

        public static string[] GetArrayValue(string metadata, string keyName)
        {
            string value = GetValue(metadata, keyName);
            value = value.TrimStart('[').TrimEnd(']');
            return value.Split(", ");
        }
    }
}
