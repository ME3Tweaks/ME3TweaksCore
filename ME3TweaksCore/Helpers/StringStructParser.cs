using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorerCore.Misc;
using ME3TweaksCore.Diagnostics;
using ME3TweaksCore.Localization;
using Serilog;

namespace ME3TweaksCore.Helpers
{
    /// <summary>
    /// Parser class for different types of string structs and list
    /// </summary>
    public static class StringStructParser
    {
        /// <summary>
        /// Gets a list of strings that are split by ;, or a custom separator character.
        /// </summary>
        /// <param name="inputString"></param>
        /// <returns></returns>
        public static List<string> GetSemicolonSplitList(string inputString, char separateChar = ';')
        {
            // 06/06/2024 Changed to TrimSymetrical. Revert if this breaks stuff!
            // inputString = inputString.Trim('(', ')');
            inputString = inputString.TrimSymetrical(int.MaxValue, '(', ')');
            return inputString.Split(separateChar).ToList();
        }

        // This is here because I wanted names to be easier to read on these
        /// <summary>
        /// Gets a list of strings that are split by ,. Accepts one incoming set of ( and )
        /// </summary>
        /// <param name="inputString"></param>
        /// <returns></returns>
        public static List<string> GetCommaSplitList(string inputString)
        {
            return GetSemicolonSplitList(inputString, ',');
        }

        /// <summary>
        /// Builds a list resulting in the form of (Property=Value, Property2=Value2, 
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public static string BuildCommaSeparatedSplitValueList(Dictionary<string, string> keys, params string[] keyValuesToQuote)
        {
            // 06/07/2024 - Repoint to customizable method
            return BuildSeparatedSplitValueList(keys, ',', '(', ')', true, keyValuesToQuote);
        }

        public static string BuildSeparatedSplitValueList(Dictionary<string, string> keys, char separator, char openChar, char closeChar, bool quoteValues = true, params string[] keyValuesToQuote)
        {
            string str = openChar.ToString();
            bool first = true;
            foreach (var kp in keys)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    str += separator;
                }
                str += kp.Key;
                str += @"=";
                if (!quoteValues || (!kp.Value.Contains(@" ") && !keyValuesToQuote.Contains(kp.Key, StringComparer.InvariantCultureIgnoreCase)))
                {
                    str += kp.Value;
                }
                else
                {
                    // values with spaces need quoted
                    str += $"\"{kp.Value}\""; //do not localize
                }
            }
            return str + closeChar;
        }

        /// <summary>
        /// Builds a multi-same-key supported list resulting in the form of (Property=Value, Property2=Value2)
        /// </summary>
        /// <param name="keys"></param>
        /// <returns></returns>
        public static string BuildCommaSeparatedSplitMultiValueList(Dictionary<string, List<string>> keys, char startChar = '(', char endChar = ')', bool quoteValues = true, params string[] keyValuesToQuote)
        {
            string str = startChar.ToString();
            bool first = true;
            foreach (var kp in keys)
            {
                foreach (var val in kp.Value)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        str += @",";
                    }

                    str += kp.Key;
                    str += @"=";
                    if (!quoteValues || (!val.Contains(@" ") && !keyValuesToQuote.Contains(kp.Key, StringComparer.InvariantCultureIgnoreCase)))
                    {
                        str += val;
                    }
                    else
                    {
                        // values with spaces need quoted
                        str += $"\"{val}\""; //do not localize
                    }
                }
            }
            return str + endChar;
        }

        /// <summary>
        /// THIS IS THE OLD WAY, SHOULD PROBABLY STOP USING IT.
        /// Gets a dictionary of command split value keypairs. Can accept incoming string with 1 outer parenthesis at most.
        /// </summary>
        /// <param name="inputString">The string to split to value</param>
        /// <param name="canBeCaseInsensitive">If the keys can be case insensitive. This changes the return to a case insensitive dictionary type, casted to Dictionary</param>
        /// <returns></returns>
        public static Dictionary<string, string> GetCommaSplitValues(string inputString, bool canBeCaseInsensitive = false)
        {
            // Changing this is REAL bad idea
            // Don't do it! Fight the urge!
#if DEBUG
            var origString = inputString;
#endif
            inputString = inputString.TrimEnd(';'); // I don't know why bioware does shit like this

            if (inputString[0] == '(' && inputString[1] == '(' && inputString[inputString.Length - 1] == ')' && inputString[inputString.Length - 2] == ')')
            {
                throw new Exception(@"GetCommaSplitValues() can only deal with items encapsulated in a single ( ) set. The current set has at least two, e.g. ((value)).");
            }

            inputString = inputString.Trim('(', ')');
            //Find commas
            int propNameStartPos = 0;
            int lastEqualsPos = -1;

            int openingQuotePos = -1; //quotes if any
            int closingQuotePos = -1; //quotes if any
            bool isInQuotes = false;

            int openParenthesisCount = 0;
            var values = canBeCaseInsensitive ? new CaseInsensitiveDictionary<string>() : new Dictionary<string, string>();
            for (int i = 0; i < inputString.Length; i++)
            {
#if DEBUG
                // See what's parsing
                string currentStr = inputString.Substring(i);
#endif
                switch (inputString[i])
                {
                    case ')':
                        if (openParenthesisCount <= 0)
                        {
                            throw new Exception(@"ASSERT ERROR: StringStructParser cannot handle closing ) without an opening (. at position " + i);
                        }
                        //closingParenthesisPos = i;
                        openParenthesisCount--;
                        break;
                    case '(':
                        openParenthesisCount++;
                        break;
                    case '"':
                        if (openingQuotePos != -1)
                        {
                            closingQuotePos = i;
                            isInQuotes = false;
                        }
                        else
                        {
                            openingQuotePos = i;
                            isInQuotes = true;
                        }
                        break;
                    case '=':
                        // 07/10/2024 only track first = so it doesn't take substructs in the values.
                        if (lastEqualsPos == -1 && !isInQuotes && openParenthesisCount <= 0)
                        {
                            lastEqualsPos = i;
                        }
                        break;
                    case ',':
                        if (!isInQuotes && openParenthesisCount <= 0)
                        {
                            //New property
                            {
                                if (lastEqualsPos < propNameStartPos) throw new Exception(@"ASSERT ERROR: Error parsing string struct: equals cannot come before property name start. Value: " + inputString);
                                string propertyName = inputString.Substring(propNameStartPos, lastEqualsPos - propNameStartPos).Trim();
                                string value = "";
                                if (openingQuotePos >= 0 && closingQuotePos > openingQuotePos)
                                {
                                    value = inputString.Substring(openingQuotePos + 1, closingQuotePos - (openingQuotePos + 1)).Trim();
                                }
                                else
                                {
                                    value = inputString.Substring(lastEqualsPos + 1, i - (lastEqualsPos + 1)).Trim();
                                }
                                values[propertyName] = value;
                            }
                            //Reset values
                            propNameStartPos = i + 1;
                            lastEqualsPos = -1;
                            openingQuotePos = -1; //quotes if any
                            closingQuotePos = -1; //quotes if any
                        }
                        break;
                    //todo: Ignore quoted items to avoid matching a ) on quotes
                    default:

                        //do nothing
                        break;
                }
            }
            //Finish last property
            {
                if (lastEqualsPos > -1) // If the struct is empty there won't be a last equals position
                {
                    string propertyName = inputString.Substring(propNameStartPos, lastEqualsPos - propNameStartPos)
                        .Trim();
                    string value = "";
                    if (openingQuotePos >= 0 && closingQuotePos > openingQuotePos)
                    {
                        value = inputString.Substring(openingQuotePos + 1, closingQuotePos - (openingQuotePos + 1))
                            .Trim();
                    }
                    else
                    {
                        value = inputString.Substring(lastEqualsPos + 1, inputString.Length - (lastEqualsPos + 1))
                            .Trim();
                    }

                    values[propertyName] = value;
                }
            }
            return values;
        }

        /// <summary>
        /// Trims an equal number of characters off each end of the string.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string TrimSymetrical(this string str, int maxCuts = int.MaxValue, params char[] chars)
        {
            string outStr = str;

            // To evenly cut out we must have at least 2 chars remaining, as it could cut down to empty.
            while (outStr.Length > 1 && maxCuts > 0)
            {
                if (chars.Contains(outStr[0]) && chars.Contains(outStr[^1]))
                {
                    outStr = outStr[1..^1];
                    maxCuts--;
                }
                else
                {
                    break;
                }
            }

            return outStr;
        }

        /// <summary>
        /// NEW WAY
        /// Gets a dictionary of command split value keypairs. This version does not accept duplicate keys. Can accept incoming string with 1 outer parenthesis at most.
        /// </summary>
        /// <param name="inputString">The string to split to value</param>
        /// <param name="canBeCaseInsensitive">If the keys can be case insensitive. This changes the return to a case insensitive dictionary type, casted to Dictionary</param>
        /// <returns></returns>
        public static Dictionary<string, string> GetSplitMapValues(string inputString, bool canBeCaseInsensitive = false, char openChar = '(', char closeChar = ')', char openChar2 = Char.MinValue, char closeChar2 = Char.MinValue)
        {
#if DEBUG
            var origString = inputString;
#endif
            inputString = inputString.TrimEnd(';'); // I don't know why bioware does shit like this

            if (inputString[0] == openChar && inputString[1] == openChar && inputString[^1] == closeChar && inputString[^2] == closeChar)
            {
                throw new Exception(@"GetSplitMapValues() can only deal with items encapsulated in a single set of opening and closing characters. The current set has at least two, e.g. ((value)) or [[value]].");
            }

            inputString = inputString.TrimSymetrical(1, openChar, closeChar);
            //Find commas
            int propNameStartPos = 0;
            int lastEqualsPos = -1;

            int openingQuotePos = -1; //quotes if any
            int closingQuotePos = -1; //quotes if any
            bool isInQuotes = false;

            int openParenthesisCount = 0;
            var values = canBeCaseInsensitive ? new CaseInsensitiveDictionary<string>() : new Dictionary<string, string>();
            for (int i = 0; i < inputString.Length; i++)
            {
#if DEBUG
                var remainingString = inputString.Substring(i);
#endif
                // Variables
                if (inputString[i] == closeChar || (closeChar2 != char.MinValue && inputString[i] == closeChar2))
                {
                    if (openParenthesisCount <= 0)
                    {
                        throw new Exception($@"ASSERT ERROR: StringStructParser cannot handle closing {closeChar} without an opening {openChar} (or {openChar2}) - at position {i}");
                    }

                    //closingParenthesisPos = i;
                    openParenthesisCount--;
                    continue;
                }

                if (inputString[i] == openChar)
                {
                    openParenthesisCount++;
                    continue;
                }

                if (openChar2 != char.MinValue && inputString[i] == openChar2)
                {
                    openParenthesisCount++;
                    continue;
                }

                // Constants
                switch (inputString[i])
                {

                    case '"':
                        if (openingQuotePos != -1)
                        {
                            closingQuotePos = i;
                            isInQuotes = false;
                        }
                        else
                        {
                            openingQuotePos = i;
                            isInQuotes = true;
                        }
                        break;
                    case '=':
                        if (lastEqualsPos == -1 && !isInQuotes && openParenthesisCount <= 0)
                        {
                            lastEqualsPos = i;
                        }
                        break;
                    case ',':
                        if (!isInQuotes && openParenthesisCount <= 0)
                        {
                            //New property
                            {
                                if (lastEqualsPos < propNameStartPos && lastEqualsPos != -1) throw new Exception(@"ASSERT ERROR: Error parsing string struct: equals cannot come before property name start. Value: " + inputString);
                                if (lastEqualsPos == -1 && propNameStartPos >= 0) throw new Exception(@"ASSERT ERROR: Error parsing string struct: Could not find equals since parsing the previous property (if any). Value: " + inputString);
                                string propertyName = inputString.Substring(propNameStartPos, lastEqualsPos - propNameStartPos).Trim();
                                string value = "";
                                if (openingQuotePos >= 0)
                                {
                                    value = inputString.Substring(openingQuotePos + 1, closingQuotePos - (openingQuotePos + 1)).Trim();
                                }
                                else
                                {
                                    value = inputString.Substring(lastEqualsPos + 1, i - (lastEqualsPos + 1)).Trim();
                                }

                                // Will throw exception on duplicate
                                values.Add(propertyName, value);
                            }
                            //Reset values
                            propNameStartPos = i + 1;
                            lastEqualsPos = -1;
                            openingQuotePos = -1; //quotes if any
                            closingQuotePos = -1; //quotes if any
                        }
                        break;
                    //todo: Ignore quoted items to avoid matching a ) on quotes
                    default:

                        //do nothing
                        break;
                }
            }
            //Finish last property
            {
                if (lastEqualsPos > -1) // If the struct is empty there won't be a last equals position
                {
                    string propertyName = inputString.Substring(propNameStartPos, lastEqualsPos - propNameStartPos)
                        .Trim();
                    string value = "";
                    if (openingQuotePos >= 0)
                    {
                        value = inputString.Substring(openingQuotePos + 1, closingQuotePos - (openingQuotePos + 1))
                            .Trim();
                    }
                    else
                    {
                        value = inputString.Substring(lastEqualsPos + 1, inputString.Length - (lastEqualsPos + 1))
                            .Trim();
                    }

                    values.Add(propertyName, value);
                }
            }
            return values;
        }

        /// <summary>
        /// NEW WAY
        /// Gets a dictionary of command split value keypairs. This version accepts multiple duplicate keys, as results are stored in lists. Can accept incoming string with 1 outer parenthesis at most.
        /// </summary>
        /// <param name="inputString">The string to split to value</param>
        /// <param name="canBeCaseInsensitive">If the keys can be case insensitive. This changes the return to a case insensitive dictionary type, casted to Dictionary</param>
        /// <returns></returns>
        public static Dictionary<string, List<string>> GetSplitMultiMapValues(string inputString, bool canBeCaseInsensitive = false, char openChar = '(', char closeChar = ')')
        {
#if DEBUG
            var origString = inputString;
#endif
            inputString = inputString.TrimEnd(';'); // I don't know why bioware does shit like this

            if (inputString[0] == openChar && inputString[1] == openChar && inputString[^1] == closeChar && inputString[^2] == closeChar)
            {
                throw new Exception(@"GetSplitMultiValues() can only deal with items encapsulated in a single set of opening and closing characters. The current set has at least two, e.g. ((value)) or [[value]].");
            }

            inputString = inputString.TrimSymetrical(1, openChar, closeChar);
            //Find commas
            int propNameStartPos = 0;
            int lastEqualsPos = -1;

            int openingQuotePos = -1; //quotes if any
            int closingQuotePos = -1; //quotes if any
            bool isInQuotes = false;

            int openParenthesisCount = 0;
            var values = canBeCaseInsensitive ? new CaseInsensitiveDictionary<List<string>>() : new Dictionary<string, List<string>>();
            for (int i = 0; i < inputString.Length; i++)
            {
#if DEBUG
                var remainingString = inputString.Substring(i);
#endif
                // Variables
                if (inputString[i] == closeChar)
                {
                    if (openParenthesisCount <= 0)
                    {
                        throw new Exception($@"ASSERT ERROR: StringStructParser cannot handle closing {closeChar} without an opening {openChar} - at position {i}");
                    }

                    //closingParenthesisPos = i;
                    openParenthesisCount--;
                    continue;
                }

                if (inputString[i] == openChar)
                {
                    openParenthesisCount++;
                    continue;
                }

                // Constants
                switch (inputString[i])
                {

                    case '"':
                        if (openingQuotePos != -1)
                        {
                            closingQuotePos = i;
                            isInQuotes = false;
                        }
                        else
                        {
                            openingQuotePos = i;
                            isInQuotes = true;
                        }
                        break;
                    case '=':
                        if (!isInQuotes && openParenthesisCount <= 0)
                        {
                            lastEqualsPos = i;
                        }
                        break;
                    case ',':
                        if (!isInQuotes && openParenthesisCount <= 0)
                        {
                            //New property
                            {
                                if (lastEqualsPos < propNameStartPos && lastEqualsPos != -1) throw new Exception(@"ASSERT ERROR: Error parsing string struct: equals cannot come before property name start. Value: " + inputString);
                                if (lastEqualsPos == -1 && propNameStartPos >= 0) throw new Exception(@"ASSERT ERROR: Error parsing string struct: Could not find equals since parsing the previous property (if any). Value: " + inputString);
                                string propertyName = inputString.Substring(propNameStartPos, lastEqualsPos - propNameStartPos).Trim();
                                string value = "";
                                if (openingQuotePos >= 0)
                                {
                                    value = inputString.Substring(openingQuotePos + 1, closingQuotePos - (openingQuotePos + 1)).Trim();
                                }
                                else
                                {
                                    value = inputString.Substring(lastEqualsPos + 1, i - (lastEqualsPos + 1)).Trim();
                                }

                                if (!values.TryGetValue(propertyName, out var list))
                                {
                                    list = new List<string>();
                                    values[propertyName] = list;
                                }
                                list.Add(value);
                            }
                            //Reset values
                            propNameStartPos = i + 1;
                            lastEqualsPos = -1;
                            openingQuotePos = -1; //quotes if any
                            closingQuotePos = -1; //quotes if any
                        }
                        break;
                    //todo: Ignore quoted items to avoid matching a ) on quotes
                    default:

                        //do nothing
                        break;
                }
            }
            //Finish last property
            {
                if (lastEqualsPos > -1) // If the struct is empty there won't be a last equals position
                {
                    string propertyName = inputString.Substring(propNameStartPos, lastEqualsPos - propNameStartPos)
                        .Trim();
                    string value = "";
                    if (openingQuotePos >= 0)
                    {
                        value = inputString.Substring(openingQuotePos + 1, closingQuotePos - (openingQuotePos + 1))
                            .Trim();
                    }
                    else
                    {
                        value = inputString.Substring(lastEqualsPos + 1, inputString.Length - (lastEqualsPos + 1))
                            .Trim();
                    }

                    if (!values.TryGetValue(propertyName, out var list))
                    {
                        list = new List<string>();
                        values[propertyName] = list;
                    }
                    list.Add(value);
                }
            }
            return values;
        }

        /// <summary>
        /// Gets a list of parenthesis splitvalues - items such as (...),(...),(...), the list of ... items are returned.
        /// </summary>
        /// <param name="inputString"></param>
        /// <returns></returns>
        public static List<string> GetParenthesisSplitValues(string inputString)
        {
            var origString = inputString;
            //Trim ends if this is a list as ( ) will encapsulte a list of ( ) values, e.g. ((hello),(there)) => (hello),(there)
            if (inputString.Length >= 4)
            {
                if (inputString[0] == '(' && inputString[1] == '(' && inputString[^1] == ')' && inputString[^2] == ')')
                {
                    //Debug.WriteLine(inputString);
                    inputString = inputString.Substring(1, inputString.Length - 2);
                    //Debug.WriteLine(inputString);
                }
            }
            //Debug.WriteLine(inputString);
            //Find matching parenthesis
            Stack<(char c, int pos)> parenthesisStack = new Stack<(char c, int pos)>();
            List<string> splits = new List<string>();
            bool quoteOpen = false;
            for (int i = 0; i < inputString.Length; i++)
            {
                //Debug.WriteLine(inputString[i]);
                switch (inputString[i])
                {
                    case '(':
                        if (!quoteOpen)
                        {
                            parenthesisStack.Push((inputString[i], i));
                        }

                        break;
                    case ')':
                        if (!quoteOpen)
                        {
                            if (parenthesisStack.Count == 0)
                            {
                                MLog.Error(@"Error parsing parenthesis split list: Found closing parenthesis that does not match open parenthesis at position " + i);
                                throw new Exception(LC.GetString(LC.string_interp_ssp_unopenedParenthsisFound, i, inputString)); //should this be localized?
                            }

                            var popped = parenthesisStack.Pop();
                            if (parenthesisStack.Count == 0)
                            {
                                //Matching brace found
                                string splitval = inputString.Substring(popped.pos, i - popped.pos + 1);
                                //Debug.WriteLine(splitval);

                                splits.Add(splitval); //This will include the ( )
                            }
                        }

                        break;
                    case '\"':
                        //Used to ignore ( ) inside of a quoted string
                        quoteOpen = !quoteOpen;
                        break;
                }
            }
            if (parenthesisStack.Count > 0)
            {
                MLog.Error(@"Error parsing parenthesis split list: count of open and closing parenthesis does not match.");
                throw new Exception(LC.GetString(LC.string_interp_ssp_unclosedParenthesisFound, origString)); //should this be localized?
            }
            return splits;
        }
    }
}
