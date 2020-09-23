using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Task = System.Threading.Tasks.Task;

namespace AutoDoxyDoc
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class GenerateComment
    {
        [Import]
        public IVsEditorAdaptersFactoryService AdapterService = null;

        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("dc4e4bbc-7b8f-44b5-b27c-2f2d9a599ee9");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="GenerateComment"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private GenerateComment(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static GenerateComment Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Switch to the main thread - the call to AddCommand in GenerateComment's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new GenerateComment(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;

            if (dte == null)
            {
                return;
            }

            TextSelection ts = dte.ActiveDocument.Selection as TextSelection;

            // Scroll down until we find a non-comment line.
            ts.EndOfLine();
            ScrollToCode(ts);
            ts.EndOfLine();

            // Search for the associated code element.
            CodeElement codeElement = null;
            FileCodeModel fcm = dte.ActiveDocument.ProjectItem.FileCodeModel;
            if (fcm != null)
            {
                while (codeElement == null)
                {
                    codeElement = fcm.CodeElementFromPoint(ts.ActivePoint, vsCMElement.vsCMElementFunction);

                    // TODO: Checking for code function here messes up class comments, but
                    // it ensures that when the function declaration is on multiple lines,
                    // we don't mistakenly generate comment for the parent class.
                    if (codeElement == null)// || !(codeElement is CodeFunction))
                    {
                        codeElement = null;
                        ts.LineDown();
                        ts.EndOfLine();
                    }
                }
            }

            var elemStartPoint = codeElement.StartPoint;

            // Determine indentation level from the code element's start point.
            string refLine = ts.ActivePoint.CreateEditPoint().GetLines(elemStartPoint.Line, elemStartPoint.Line + 1);
            string spaces = refLine.Replace(refLine.TrimStart(), "");

            // Extract existing comment if found.
            ts.MoveToLineAndOffset(elemStartPoint.Line, elemStartPoint.LineCharOffset);
            int startLine = ExtractComment(ref ts, out string existingDoxyComment);

            // Delete old comment from the text.
            int oldLine = elemStartPoint.Line;
            int oldOffset = elemStartPoint.LineCharOffset;

            if (startLine >= 0)
            {
                ts.ActivePoint.CreateEditPoint().Delete(elemStartPoint);
                oldLine = ts.ActivePoint.Line;
                oldOffset = ts.ActivePoint.LineCharOffset;
            }

            // Generate new comment.
            string doxyComment = m_generator.GenerateComment(spaces, codeElement, existingDoxyComment);

            // Write the doxygen comment to the correct position.
            ts.MoveToLineAndOffset(elemStartPoint.Line, elemStartPoint.LineCharOffset);
            ts.StartOfLine();
            ts.Insert(spaces + doxyComment + "\r\n");

            //if (startLine < 0)
            {
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.LineDown();
                ts.EndOfLine();
            }
        }

        private int ExtractComment(ref TextSelection ts, out string comment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            comment = "";
            string curLine = "";
            int startLine = -1;
            int endLine = -1;

            do
            {
                ts.LineUp();
                curLine = ts.ActivePoint.CreateEditPoint().GetLines(ts.ActivePoint.Line, ts.ActivePoint.Line + 1);
                curLine = curLine.TrimStart();

                // Check if we found the beginning of the comment.
                if (curLine.StartsWith("/*!"))
                {
                    startLine = ts.ActivePoint.Line;
                    break;
                }
                // Check for the end of the comment.
                else if (curLine.StartsWith("*/"))
                {
                    endLine = ts.ActivePoint.Line;
                }

            } while (curLine.Length == 0 || curLine.StartsWith("*"));

            if (startLine >= 0 && endLine >= ts.ActivePoint.Line)
            {
                comment = ts.ActivePoint.CreateEditPoint().GetLines(startLine, endLine + 1);
            }

            return startLine;
        }

        private void ScrollToCode(TextSelection ts)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string curLine = ts.ActivePoint.CreateEditPoint().GetLines(ts.ActivePoint.Line, ts.ActivePoint.Line + 1);
            curLine = curLine.TrimStart();

            while (curLine.Length == 0 || curLine.StartsWith("/*!") || curLine.StartsWith("*") || curLine.StartsWith("//"))
            {
                ts.LineDown();
                curLine = ts.ActivePoint.CreateEditPoint().GetLines(ts.ActivePoint.Line, ts.ActivePoint.Line + 1);
                curLine = curLine.TrimStart();
            }
        }

        //! Doxygen generator.
        private DoxygenGenerator m_generator = new DoxygenGenerator(new DoxygenConfig());
    }
}
