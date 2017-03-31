using Mono.Debugging.Win32;
using System;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Microsoft.Samples.Debugging.CorDebug;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Linq;
using Microsoft.Metadata.Tools;

namespace DebugTest
{
    public class PdbSymbolReaderFactory : ICustomCorSymbolReaderFactory
    {
        private bool IsPortablePdbFormat(string fileName)
        {
            using (var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Read first 4bytes and check if it matched portable pdb files.
                return (int)new BinaryReader(fileStream).ReadUInt32() == 1112167234;
            }
        }

        public ISymbolReader CreateCustomSymbolReader(string assemblyLocation)
        {
            if(!File.Exists(assemblyLocation))
            {
                return null;
            }

            string pdbLocation = Path.ChangeExtension(assemblyLocation, "pdb");

            if(!File.Exists(pdbLocation))
            {
                return null;
            }

            if(!IsPortablePdbFormat(pdbLocation))
            {
                return null;
            }

            var provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbLocation));

            var pdbReader = provider.GetMetadataReader();

            var visualizer = new MetadataVisualizer(pdbReader, null, MetadataVisualizerOptions.NoHeapReferences);

            return new PdbSymbolReader(visualizer);
        }
    }
}
