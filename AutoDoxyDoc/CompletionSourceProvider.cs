using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace AutoDoxyDoc
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("Code")]
    [Name("Doxygen tag completion")]
    public class CompletionSourceProvider : ICompletionSourceProvider
    {
        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        [Import]
        internal IGlyphService GlyphService { get; set; }

        /// <summary>
        /// Tries to create a Doxygen completion source.
        /// </summary>
        /// <param name="textBuffer"></param>
        /// <returns></returns>
        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            return new DoxygenCompletionSource(this, textBuffer);
        }
    }
}