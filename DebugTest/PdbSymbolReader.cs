using DebugTest.PdbParser;
using Microsoft.DiaSymReader.PortablePdb;
using Microsoft.Metadata.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Reflection.Metadata;

namespace DebugTest
{
    public static class Extensions
    {
        public static bool IsWithin(this SequencePoint point, UInt32 line, UInt32 column)
        {
            if(point.StartLine == line)
            {
                if(0 < column && point.StartColumn > column)
                {
                    return false;
                }
            }

            if(point.EndLine == line)
            {
                if(point.EndColumn < column)
                {
                    return false;
                }
            }

            if(!((point.StartLine <= line) && (point.EndLine >= line)))
            {
                return false;
            }

            return true;
        }

        public static bool IsWithinLineOnly(this SequencePoint point, UInt32 line)
        {
            return ((point.StartLine <= line) && (line <= point.EndLine));
        }

        public static bool IsGreaterThan(this SequencePoint point, UInt32 line, UInt32 column)
        {
            return (point.StartLine > line) || (point.StartLine == line && point.StartColumn > column);
        }

        public static bool IsLessThan (this SequencePoint point, UInt32 line, UInt32 column)
        {
            return (point.StartLine < line) || (point.StartLine == line && point.StartColumn < column);
        }
    }

    public class PdbSymbolReader : ISymbolReader
    {
        private MetadataVisualizer _visualizer;

        private List<SymbolMethod> _methods;
        
        private Dictionary<string, ISymbolDocument> _documentLookup;

        private List<SymbolDocument> _documents;

        public PdbSymbolReader(MetadataVisualizer visualizer)
        {
            _visualizer = visualizer;

            _documents = new List<SymbolDocument>();
            _documentLookup = new Dictionary<string, ISymbolDocument>();
            _methods = new List<SymbolMethod>();

            foreach(var document in _visualizer.GetDocuments())
            {
                _documents.Add(document);
                _documentLookup.Add(document.URL, document);
            }

            foreach(var method in _visualizer.GetMethodDebugInformation())
            {
                if (method.DocumentRowId > 0)
                {
                    var document = _documents[method.DocumentRowId - 1];

                    method.Document = document;

                    _methods.Add(method);
                }
            }
        }

        public SymbolToken UserEntryPoint => throw new NotImplementedException();

        public ISymbolDocument GetDocument(string url, Guid language, Guid languageVendor, Guid documentType)
        {
            throw new NotImplementedException();
        }

        public ISymbolDocument[] GetDocuments()
        {
            return _visualizer.GetDocuments().ToArray();
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
            // See c++ implementation here... https://github.com/dotnet/coreclr/blob/master/src/debug/ildbsymlib/symread.cpp
            bool found = false;
            ISymbolMethod result = null;
            
            foreach (var method in _methods)
            {
                SequencePoint sequencePointBefore;
                SequencePoint sequencePointAfter;

                if(method.DocumentRowId > 0 && _documents[method.DocumentRowId-1].CompareTo(document) == 0)
                {
                    foreach (var point in method.SequencePoints)
                    {
                        if(point.IsWithin((uint)line, (uint)column))
                        {
                            found = true;
                            result = method;
                            break;
                        }
                    }

                    if(found)
                    {
                        break;
                    }
                }
            }

            return result;
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
