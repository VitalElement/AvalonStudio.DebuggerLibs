using System;
using System.Diagnostics.SymbolStore;

namespace DebugTest
{
    public class PdbSymbolReader : ISymbolReader
    {
        public SymbolToken UserEntryPoint => throw new NotImplementedException();

        public ISymbolDocument GetDocument(string url, Guid language, Guid languageVendor, Guid documentType)
        {
            throw new NotImplementedException();
        }

        public ISymbolDocument[] GetDocuments()
        {
            throw new NotImplementedException();
        }

        public ISymbolVariable[] GetGlobalVariables()
        {
            throw new NotImplementedException();
        }

        public ISymbolMethod GetMethod(SymbolToken method)
        {
            throw new NotImplementedException();
        }

        public ISymbolMethod GetMethod(SymbolToken method, int version)
        {
            throw new NotImplementedException();
        }

        public ISymbolMethod GetMethodFromDocumentPosition(ISymbolDocument document, int line, int column)
        {
            throw new NotImplementedException();
        }

        public ISymbolNamespace[] GetNamespaces()
        {
            throw new NotImplementedException();
        }

        public byte[] GetSymAttribute(SymbolToken parent, string name)
        {
            throw new NotImplementedException();
        }

        public ISymbolVariable[] GetVariables(SymbolToken parent)
        {
            throw new NotImplementedException();
        }
    }
}
