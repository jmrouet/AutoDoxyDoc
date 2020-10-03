﻿using System;

namespace AutoDoxyDoc
{
    public enum DoxygenStyle
    {
        Qt,
        JavaDoc
    };

    public class DoxygenConfig
    {
        /// <summary>
        /// Event that is triggered when the config has changed.
        /// </summary>
        public event EventHandler ConfigChanged;

        /// <summary>
        /// Amount of indentation spaces for doxygen tags.
        /// </summary>
        public int TagIndentation { get; set; } = 4;

        /// <summary>
        /// Comment tag style.
        /// </summary>
        public DoxygenStyle TagStyle { get; set; } = DoxygenStyle.JavaDoc;

        /// <summary>
        /// File comment template.
        /// </summary>
        public string FileCommentTemplate { get; set; } = "";

        /// <summary>
        /// Tag starting character convenience getter.
        /// </summary>
        public char TagChar
        {
            get
            {
                switch (TagStyle)
                {
                    case DoxygenStyle.JavaDoc:
                    default:
                        return '@';

                    case DoxygenStyle.Qt:
                        return '\\';
                }
            }
        }

        /// <summary>
        /// If true, auto-generation tries to generate smart comments for function summary, parameters and return values.
        /// </summary>
        public bool SmartComments { get; set; } = true;

        /// <summary>
        /// If true, auto-generation creates smart comments for all kinds of functions.
        /// </summary>
        public bool SmartCommentsForAllFunctions { get; set; } = true;

        /// <summary>
        /// Abbreviations collection for unabbreviating words.
        /// </summary>
        public AbbreviationMap Abbreviations { get; set; } = new AbbreviationMap();

        /// <summary>
        /// Formatting for autogenerated comments.
        /// </summary>
        public string BriefSetterDescFormat { get; set; } = "Sets the {1}{0}.";
        public string BriefGetterDescFormat { get; set; } = "Returns the {1}{0}.";
        public string BriefBoolGetterDescFormat { get; set; } = "Returns true if the {1}{2} {0}.";
        public string ParamSetterDescFormat { get; set; } = "{0} to set.";
        public string ReturnDescFormat { get; set; } = "The {0}.";
        public string ReturnBooleanDescFormat { get; set; } = "True if {0}. False if not.";
        public string ParamBooleanFormat { get; set; } = "If true, {0}. Otherwise not {0}.";
        public string FileCommentIsHeader { get; set; } = "Declares the {0}.";
        public string FileCommentIsSource { get; set; } = "Implements the {0}.";
        public string FileCommentIsInline { get; set; } = "Implements the {0}.";

        /// <summary>
        /// Constructor.
        /// </summary>
        public DoxygenConfig()
        {
        }

        /// <summary>
        /// Loads configuration from options page.
        /// </summary>
        /// <param name="options">The options page.</param>
        public void LoadSettings(OptionsPage options)
        {
            TagIndentation = options.TagIndentation;
            TagStyle = options.TagStyle;
            FileCommentTemplate = options.FileCommentTemplate;
            SmartComments = options.SmartComments;
            SmartCommentsForAllFunctions = options.SmartCommentsForAllFunctions;
            Abbreviations = options.Abbreviations;
            BriefSetterDescFormat = options.BriefSetterDescFormat;
            BriefGetterDescFormat = options.BriefGetterDescFormat;
            BriefBoolGetterDescFormat = options.BriefBoolGetterDescFormat;
            ParamSetterDescFormat = options.ParamSetterDescFormat;
            ReturnDescFormat = options.ReturnDescFormat;
            ReturnBooleanDescFormat = options.ReturnBooleanDescFormat;
            ParamBooleanFormat = options.ParamBooleanFormat;
            FileCommentIsHeader = options.FileCommentIsHeader;
            FileCommentIsSource = options.FileCommentIsSource;
            FileCommentIsInline = options.FileCommentIsInline;

            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
