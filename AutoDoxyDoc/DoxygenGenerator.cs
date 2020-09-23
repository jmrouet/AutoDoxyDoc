using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoDoxyDoc
{

    class DoxygenGenerator
    {
        public DoxygenGenerator(DoxygenConfig config)
        {
            m_config = config;
            m_config.ConfigChanged += onConfigChanged;
            InitStyle();
        }

        /// <summary>
        /// Generates smart indentation for the current line.
        /// </summary>
        /// <param name="curOffset">Char offset on the current line.</param>
        /// <param name="prevLine">Contents of the previous line.</param>
        /// <param name="newOffset">Calculated new char offset on the current line.</param>
        /// <returns>True if smart indentation determined proper indentation. False if not.</returns>
        public bool GenerateIndentation(int curOffset, string prevLine, out int newOffset)
        {
            newOffset = curOffset;
            Match paramMatch = m_regexParam.Match(prevLine);
            Match tagMatch = m_regexTagSection.Match(prevLine);

            if (paramMatch.Success)
            {
                var commentCapture = paramMatch.Groups[3];
                int diff = commentCapture.Index - curOffset;

                if (diff > 0)
                {
                    newOffset = commentCapture.Index + 1;
                    return true;
                }
            }
            else if (tagMatch.Success)
            {
                var commentCapture = tagMatch.Groups[2];
                int diff = commentCapture.Index - curOffset;

                if (diff > 0)
                {
                    newOffset = commentCapture.Index + 1;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Generates a doxygen comment block for the given code element.
        /// </summary>
        /// <param name="spaces">Indentation for the whole comment block</param>
        /// <param name="codeElement">The code element.</param>
        /// <param name="existingComment">Existing comment for the code element. Empty if not found.</param>
        /// <returns>Generated doxygen comment block</returns>
        public string GenerateComment(string spaces, CodeElement codeElement, string existingComment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Parse existing comment.
            ParsedComment parsedComment = ParseComment(existingComment);

            // Start writing a new one.
            StringBuilder sb = new StringBuilder("/*!");

            // Write main comment from existing comments, if found.
            if (parsedComment.BriefComments.Count > 0)
            {
                foreach (string line in parsedComment.BriefComments)
                {
                    sb.Append("\r\n" + spaces + " *  " + line);
                }
            }
            else
            {
                // Write placeholder for main comment.
                sb.Append("\r\n" + spaces + " *  ");

                // Try to determine initial main comment if comment auto-generation is enabled.
                if (m_config.SmartComments)
                {
                    sb.Append(TryGenerateBriefDesc(codeElement));
                }
            }

            if (codeElement != null && codeElement is CodeFunction)
            {
                CodeFunction function = codeElement as CodeFunction;
                int maxTypeDirectionLength = 0;
                int maxParamNameLength = 0;

                foreach (CodeElement child in codeElement.Children)
                {
                    CodeParameter param = child as CodeParameter;

                    if (param != null)
                    {
                        // Check if the existing comment contained this parameter.
                        ParsedParam parsedParam = null;

                        if (parsedComment.Parameters.ContainsKey(param.Name))
                        {
                            parsedParam = parsedComment.Parameters[param.Name];
                        }

                        string typeDirName = DirectionToString(GetParamDirection(param, parsedParam));
                        maxTypeDirectionLength = Math.Max(maxTypeDirectionLength, typeDirName.Length);
                        maxParamNameLength = Math.Max(maxParamNameLength, param.Name.Length);
                    }
                }

                // Create doxygen lines for each parameter.
                if (codeElement.Children.Count > 0)
                {
                    sb.Append("\r\n" + spaces + " *");

                    foreach (CodeElement child in codeElement.Children)
                    {
                        CodeParameter param = child as CodeParameter;

                        if (param != null)
                        {
                            // Check if the existing comment contained this parameter.
                            ParsedParam parsedParam = null;

                            if (parsedComment.Parameters.ContainsKey(param.Name))
                            {
                                parsedParam = parsedComment.Parameters[param.Name];
                            }

                            // Determine type of parameter (in, out or inout).
                            string typeDirName = DirectionToString(GetParamDirection(param, parsedParam));
                            string paramAlignSpaces = new string(' ', maxParamNameLength - param.Name.Length + 2);
                            string typeAlignSpaces = new string(' ', maxTypeDirectionLength - typeDirName.Length + 1);
                            string tagLine = m_indentString + m_config.TagChar + "param " + typeDirName + typeAlignSpaces + param.Name + paramAlignSpaces;
                            sb.Append("\r\n" + spaces + " *  " + tagLine);

                            // Add existing comments.
                            if (parsedParam != null)
                            {
                                AppendComments(sb, parsedParam.Comments, spaces, tagLine.Length);
                            }
                            else if (m_config.SmartComments && codeElement.Children.Count == 1)
                            {
                                sb.Append(TryGenerateParamDesc(codeElement, param));
                            }
                        }
                    }
                }

                if (function.Type.AsString != "void")
                {
                    sb.Append("\r\n" + spaces + " *");
                    string tagLine = m_indentString + m_config.TagChar + "return ";
                    sb.Append("\r\n" + spaces + " *  " + tagLine);

                    if (parsedComment.Returns != null)
                    {
                        AppendComments(sb, parsedComment.Returns.Comments, spaces, tagLine.Length);
                    }
                    else if (m_config.SmartComments)
                    {
                        sb.Append(TryGenerateReturnDesc(function));
                    }
                }
            }

            // Write other sections that were in the existing comments.
            foreach (ParsedSection section in parsedComment.TagSections)
            {
                string tagLine = m_indentString + m_config.TagChar + section.TagName + " ";
                sb.Append("\r\n" + spaces + " *");
                sb.Append("\r\n" + spaces + " *  " + tagLine);
                AppendComments(sb, section.Comments, spaces, tagLine.Length);
            }

            sb.Append("\r\n" + spaces + " */");
            return sb.ToString();
        }

        /// <summary>
        /// Generates tag start line.
        /// </summary>
        /// <param name="spaces">Indentation for the comment line.</param>
        /// <returns>Generated line string.</returns>
        public string GenerateTagStartLine(string spaces)
        {
            return "\r\n" + spaces + "*  ";
        }

        private bool IsInput(CodeParameter parameter)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            bool isConst = false;
            bool isRef = false;
            string typeName = parameter.Type.AsString;
            string[] expressions = typeName.Split(' ');

            foreach (var e in expressions)
            {
                if (e == "const")
                {
                    isConst = true;
                    break;
                }
                else if (e == "&" || e == "*")
                {
                    isRef = true;
                }
            }

            if (typeName.EndsWith("&") || typeName.EndsWith("*"))
            {
                isRef = true;
            }

            return (isConst || !isRef);
        }

        /// <summary>
        /// Parses the given Doxygen comment.
        /// </summary>
        /// <param name="comment">The comment to parse.</param>
        /// <returns>Parsed comment structure.</returns>
        private ParsedComment ParseComment(string comment)
        {
            ParsedComment parsedComment = new ParsedComment();

            if (comment.Length > 0)
            {
                string[] lines = comment.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

                // Trim leading and trailing whitespace before any parsing.
                for (int i = 0; i < lines.Length; ++i)
                {
                    lines[i] = lines[i].Trim();
                }

                for (int i = 0; i < lines.Length; ++i)
                {
                    // Skip empty lines and comment start/end lines.
                    string line = lines[i];

                    if (line.Length == 0 || line == "*" || line == "/*!" || line == "*/")
                    {
                        continue;
                    }

                    // Check if this is a parameter line.
                    Match paramMatch = m_regexParam.Match(line);

                    if (paramMatch.Success)
                    {
                        string name = paramMatch.Groups[2].Value;
                        string firstComment = paramMatch.Groups[3].Value;

                        if (!parsedComment.Parameters.ContainsKey(name) && firstComment.Length > 0)
                        {
                            ParsedParam param = new ParsedParam();
                            param.Name = name;
                            param.Direction = ToDirection(paramMatch.Groups[1].Value);
                            param.Comments.Add(firstComment);
                            i = parseExtraComments(lines, i + 1, param.Comments);

                            parsedComment.Parameters.Add(param.Name, param);
                        }
                    }
                    else
                    {
                        // Otherwise check if it is some other tag.
                        Match sectionMatch = m_regexTagSection.Match(line);

                        if (sectionMatch.Success)
                        {
                            string tagName = sectionMatch.Groups[1].Value;
                            string firstComment = sectionMatch.Groups[2].Value;

                            if (firstComment.Length > 0)
                            {
                                ParsedSection section = new ParsedSection();
                                section.TagName = tagName;
                                section.Comments.Add(firstComment);
                                i = parseExtraComments(lines, i + 1, section.Comments);

                                if (section.TagName == "return" || section.TagName == "returns")
                                {
                                    parsedComment.Returns = section;
                                }
                                else
                                {
                                    parsedComment.TagSections.Add(section);
                                }
                            }
                        }
                        else
                        {
                            // If the line doesn't contain any tag, we try to extract text out of it.
                            Match textMatch = m_regexText.Match(line);

                            if (textMatch.Success)
                            {
                                parsedComment.BriefComments.Add(textMatch.Groups[1].Value);
                            }
                        }
                    }
                }
            }

            return parsedComment;
        }

        /// <summary>
        /// Determines parameter direction based on the code element and the previously parsed parameter comment line.
        /// </summary>
        /// <param name="param">Code element for the parameter.</param>
        /// <param name="parsedParam">Existing parsed parameter info, if available.</param>
        /// <returns>The parameter direction.</returns>
        private ParamDirection GetParamDirection(CodeParameter param, ParsedParam parsedParam)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ParamDirection direction = ParamDirection.In;

            if (!IsInput(param))
            {
                if (parsedParam != null && parsedParam.Direction != ParamDirection.In)
                {
                    direction = parsedParam.Direction;
                }
                else
                {
                    // By default, non-inputs are categorized as inout.
                    direction = ParamDirection.InOut;
                }
            }

            return direction;
        }

        /// <summary>
        /// Parses extra comments for a section.
        /// </summary>
        /// <param name="lines">The comment lines available for parsing.</param>
        /// <param name="startIndex">The first line to start parsing from.</param>
        /// <param name="comments">Extracted list of comments.</param>
        /// <returns>Index of the first line which was not treated as an extra comment.</returns>
        private int parseExtraComments(string[] lines, int startIndex, List<string> comments)
        {
            // Parse comment lines until we either come across a new doxygen tag or non-text line.
            int i = startIndex;

            for (; i < lines.Length; ++i)
            {
                // Check for non-text line.
                if (lines[i].Length == 0 || lines[i] == "*" || lines[i] == "*/")
                {
                    break;
                }

                // Check for any doxygen tags.
                Match tagMatch = m_regexTagSection.Match(lines[i]);

                if (tagMatch.Success)
                {
                    break;
                }

                // Check if this is text.
                Match textMatch = m_regexText.Match(lines[i]);

                if (!textMatch.Success)
                {
                    break;
                }

                comments.Add(textMatch.Groups[1].Value);
            }

            return i - 1;
        }

        /// <summary>
        /// Appends a list of comments to the string builder.
        /// </summary>
        /// <param name="sb">String builder</param>
        /// <param name="comments">List of comments to append.</param>
        /// <param name="spaces">Number of spaces for line indentation (before asterisk).</param>
        /// <param name="indentCount">Comment indentation level (after asterisk).</param>
        private void AppendComments(StringBuilder sb, List<string> comments, string spaces, int indentCount)
        {
            // First comment line as a special case since we don't need to write any indentation.
            sb.Append(comments[0]);

            // Add new lines for rest of the comments.
            string indentString = new string(' ', indentCount);

            for (int i = 1; i < comments.Count; ++i)
            {
                sb.Append("\r\n" + spaces + " *  " + indentString + comments[i]);
            }
        }

        /// <summary>
        /// Retrieves a readable class name from a code class.
        /// </summary>
        /// <param name="codeClass">Class reference.</param>
        /// <returns>The name of the class as a spaced string.</returns>
        private string GetClassName(CodeClass codeClass)
        {
            string objectName = "";
            ThreadHelper.ThrowIfNotOnUIThread();
            string[] parentWords = StringHelper.SplitCamelCase(codeClass.Name);
            bool first = true;

            for (int i = 0; i < parentWords.Length; ++i)
            {
                if (parentWords[i].Length > 1)
                {
                    if (!first)
                    {
                        objectName += " ";
                    }

                    objectName += parentWords[i];
                }
            }

            return objectName;
        }

        /// <summary>
        /// Tries to generate brief summary of the code element.
        /// </summary>
        /// <param name="codeElement">Code element for which to generate brief summary.</param>
        /// <returns>Generated text.</returns>
        private string TryGenerateBriefDesc(CodeElement codeElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string desc = "";

            if (codeElement != null && codeElement is CodeFunction)
            {
                CodeFunction function = codeElement as CodeFunction;

                if ((function.FunctionKind & vsCMFunction.vsCMFunctionConstructor) != 0)
                {
                    desc = "Constructor.";
                }
                else if ((function.FunctionKind & vsCMFunction.vsCMFunctionDestructor) != 0)
                {
                    desc = "Destructor.";
                }
                else
                {
                    string[] funcNameWords = StringHelper.SplitCamelCase(function.Name);

                    // Retrieve the parent class's name.
                    string className = "";
                    string owner = "";

                    if (function.Parent is CodeClass)
                    {
                        var codeClass = function.Parent as CodeClass;
                        className = GetClassName(codeClass);
                        owner = className + "'s ";
                    }

                    // Determine if the function is a setter or getter.
                    bool setter = funcNameWords[0] == "set";
                    bool getter = funcNameWords[0] == "get";
                    bool boolGetter = funcNameWords[0] == "is" || funcNameWords[0] == "has";

                    if (getter || setter || boolGetter)
                    {
                        // Generate the proper brief description.
                        if (funcNameWords.Length > 1)
                        {
                            if (getter)
                            {
                                desc = String.Format(m_config.BriefGetterDescFormat, UnabbreviateAndJoin(funcNameWords, 1), owner);
                            }
                            else if (setter)
                            {
                                desc = String.Format(m_config.BriefSetterDescFormat, UnabbreviateAndJoin(funcNameWords, 1), owner);
                            }
                            else if (boolGetter)
                            {
                                desc = String.Format(m_config.BriefBoolGetterDescFormat, UnabbreviateAndJoin(funcNameWords, 1), className + " ", funcNameWords[0]);
                            }
                        }
                    }
                    else if (m_config.SmartCommentsForAllFunctions)
                    {
                        // Allow smart comments for single word functions only in case they are class members.
                        // All other functions are supported.
                        if (funcNameWords.Length > 1 || function.Parent is CodeClass)
                        {
                            // We'll use the first word as the verb in the general case.
                            // Determine the third person verb ending for the verb.
                            string verb = StringHelper.GetThirdPersonVerb(funcNameWords[0]);

                            // In case a single word function name, we assume the object of the verb is the class itself.
                            string dest;
                            if (funcNameWords.Length == 1)
                            {
                                dest = className;
                            }
                            // Otherwise the object is the part which comes in the next words.
                            else
                            {
                                dest = UnabbreviateAndJoin(funcNameWords, 1);
                            }

                            desc = StringHelper.Capitalize(String.Format("{0} the {1}.", verb, dest));
                        }
                    }
                }
            }

            return desc;
        }

        /// <summary>
        /// Tries to generate parameter description comment.
        /// </summary>
        /// <param name="parent">Parent function.</param>
        /// <param name="param">Parameter for which to generate the comment.</param>
        /// <returns>Generated text.</returns>
        private string TryGenerateParamDesc(CodeElement parent, CodeParameter param)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string desc = "";

            // If the function is a setter or getter, we can try to derive a description for the parameter.
            bool setter = parent.Name.StartsWith("set");
            bool getter = parent.Name.StartsWith("get");
            bool isBoolean = param.Type.AsString == "bool";

            if (isBoolean)
            {
                string[] words = StringHelper.SplitCamelCase(param.Name);
                desc = StringHelper.Capitalize(String.Format(m_config.ParamBooleanFormat, UnabbreviateAndJoin(words)));
            }
            else if (setter || getter)
            {
                string[] words = StringHelper.SplitCamelCase(param.Name);
                desc = StringHelper.Capitalize(String.Format(m_config.ParamSetterDescFormat, UnabbreviateAndJoin(words)));
            }

            return desc;
        }

        /// <summary>
        /// Tries to generate comment for return value.
        /// </summary>
        /// <param name="function">Function for which to generate the return value comment.</param>
        /// <returns>Generated text.</returns>
        private string TryGenerateReturnDesc(CodeFunction function)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string desc = "";
            string[] words = StringHelper.SplitCamelCase(function.Name);

            if (words.Length > 1)
            {
                if (words[0] == "get")
                {
                    desc = StringHelper.Capitalize(String.Format(m_config.ReturnDescFormat, UnabbreviateAndJoin(words, 1)));
                }
                else if (words[0] == "is")
                {
                    desc = StringHelper.Capitalize(String.Format(m_config.ReturnBooleanDescFormat, UnabbreviateAndJoin(words, 1)));
                }
                else if (words[0] == "has")
                {
                    desc = StringHelper.Capitalize(String.Format(m_config.ReturnBooleanDescFormat, "has " + UnabbreviateAndJoin(words, 1)));
                }
            }

            return desc;
        }

        /// <summary>
        /// Unabbreviates a word based on a dictionary.
        /// </summary>
        /// <param name="word">Word to unabbreviate.</param>
        /// <returns>Unabbreviated word, or the original if no conversion was found for it.</returns>
        private string Unabbreviate(string word)
        {
            return m_config.Abbreviations.Unabbreviate(word);
        }

        /// <summary>
        /// Unabbreviates all words in an array and joins them together with spaces.
        /// </summary>
        /// <param name="words">Words to unabbreviate and join.</param>
        /// <param name="startIndex">Index of the first word to include.</param>
        /// <returns>The joined string.</returns>
        private string UnabbreviateAndJoin(string[] words, int startIndex = 0)
        {
            return String.Join(" ", Array.ConvertAll(words.ToArray(), i => Unabbreviate(i)), startIndex, words.Length - startIndex);
        }

        /// <summary>
        /// Initializes config dependent variables.
        /// </summary>
        private void InitStyle()
        {
            m_indentString = new string(' ', m_config.TagIndentation);
            m_regexParam = new Regex(@"\s*\*\s+\" + m_config.TagChar + @"param\s+(\[[a-z,]+\])\s+(\w+)(?:\s+(.*))?$", RegexOptions.Compiled);
            m_regexTagSection = new Regex(@"\s*\*\s+\" + m_config.TagChar + @"([a-z]+)(?:\s+(.*))?$", RegexOptions.Compiled);
        }

        /// <summary>
        /// Re-initializes config related variables when config has been changed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void onConfigChanged(object sender, EventArgs e)
        {
            InitStyle();
        }

        /// <summary>
        /// Converts parameter direction to Doxygen string.
        /// </summary>
        /// <param name="dir">Parameter direction.</param>
        /// <returns>Doxygen compatible string.</returns>
        private static string DirectionToString(ParamDirection dir)
        {
            switch (dir)
            {
                case ParamDirection.In:
                default:
                    return "[in]";

                case ParamDirection.Out:
                    return "[out]";

                case ParamDirection.InOut:
                    return "[in,out]";
            }
        }

        /// <summary>
        /// Converts Doxygen string to parameter direction.
        /// </summary>
        /// <param name="val">The string to convert.</param>
        /// <returns>The equivalent parameter direction.</returns>
        private static ParamDirection ToDirection(string val)
        {
            switch (val)
            {
                case "[in]":
                default:
                    return ParamDirection.In;

                case "[out]":
                    return ParamDirection.Out;

                case "[in,out]":
                    return ParamDirection.InOut;
            }
        }

        /// <summary>
        /// Function parameter direction options.
        /// </summary>
        private enum ParamDirection
        {
            In,
            InOut,
            Out
        }

        /// <summary>
        /// Data for a parsed parameter.
        /// </summary>
        private class ParsedParam
        {
            public string Name { get; set; } = "";
            public ParamDirection Direction { get; set; } = ParamDirection.In;
            public List<string> Comments { get; } = new List<string>();
        }

        /// <summary>
        /// Data for a parsed section.
        /// </summary>
        private class ParsedSection
        {
            public string TagName { get; set; } = "";
            public List<string> Comments { get; } = new List<string>();
        }

        /// <summary>
        /// Data for a parsed Doxygen comment.
        /// </summary>
        private class ParsedComment
        {
            public List<string> BriefComments { get; set; } = new List<string>();
            public Dictionary<string, ParsedParam> Parameters { get; } = new Dictionary<string, ParsedParam>();
            public ParsedSection Returns { get; set; }
            public List<ParsedSection> TagSections { get; } = new List<ParsedSection>();
        }

        //! Doxygen style configuration.
        private DoxygenConfig m_config;

        //! Indentation for doxygen tags.
        private string m_indentString;

        //! Helper regular expressions.
        private Regex m_regexParam;
        private Regex m_regexTagSection;
        private Regex m_regexText = new Regex(@"\s*\*\s+(.+)$", RegexOptions.Compiled);
    }
}
