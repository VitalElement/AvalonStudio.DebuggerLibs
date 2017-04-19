using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Mono.Debugging.Soft
{
	public class SourceHashChecker
	{
		static readonly Dictionary<string, Func<HashAlgorithm>> Factories = new Dictionary<string, Func<HashAlgorithm>> {
			{"SHA1", SHA1.Create},
			{"SHA256", SHA256.Create},
			{"MD5", MD5.Create},
		};
		readonly IDictionary<string, Tuple<HashAlgorithm, Func<byte[], byte[]>>> algorithms = new Dictionary<string, Tuple<HashAlgorithm, Func<byte[], byte[]>>> ();

		static readonly List<Tuple<string, string>> LineEndingReplacements = new List<Tuple<string, string>> {
			Tuple.Create("\r\n", "\n"),
			Tuple.Create ("\n", "\r\n")
		};

		public SourceHashChecker (IDictionary<string, Func<byte[], byte[]>> algoTypes)
		{

			foreach (var algoType in algoTypes) {
				Func<HashAlgorithm> func;
				if (!Factories.TryGetValue (algoType.Key, out func))
					throw new ArgumentException (string.Format ("Algoritm {0} is unknown", algoType.Key));
				algorithms[algoType.Key] = new Tuple<HashAlgorithm, Func<byte[], byte[]>> (func(), algoType.Value);
			}
		}

		public bool CheckHash (string filename, byte[] hashFromSymbolFile)
		{
			return DoWithFileStream (filename, stream => {
				foreach (var algorithm in algorithms) {
					var hashAlgorithm = algorithm.Value.Item1;
					var hashTransformer = algorithm.Value.Item2;
					if (CheckHashForContentStream (hashAlgorithm, stream, hashFromSymbolFile, hashTransformer))
						return true;
				}
				return false;
			});
		}

		public bool CheckHash (string algorithmName, string filename, byte[] hashFromSymbolFile)
		{
			Tuple<HashAlgorithm, Func<byte[], byte[]>> tuple;
			if (!algorithms.TryGetValue (algorithmName, out tuple))
				throw new ArgumentException (string.Format ("Unknown algorithm {0}", algorithmName));

			var hashAlgorithm = tuple.Item1;
			var hashTransformer = tuple.Item2;
			return DoWithFileStream (filename, stream => CheckHashForContentStream (hashAlgorithm, stream, hashFromSymbolFile, hashTransformer));
		}

		static T DoWithFileStream<T> (string filename, Func<Stream, T> func)
		{
			using (var ms = new MemoryStream ()) {
				using (var fs = new FileStream (filename, FileMode.Open)) {
					fs.CopyTo (ms);
				}
				return func (ms);
			}
		}

		static bool CheckHashForContentStream (HashAlgorithm hashAlgorithm, Stream ms, byte[] hashFromSymbolFile, Func<byte[], byte[]> hashTransformer)
		{
			ms.Seek (0, SeekOrigin.Begin);
			var transformedHash = hashTransformer (hashFromSymbolFile);
			// first compute hash for origin byte array
			if (CheckHashForContentStream (hashAlgorithm, ms, transformedHash))
				return true;

			// if origin hash doesn't match, try to calculate with several line endings
			Encoding encoding;
			string fullText;
			ms.Seek (0, SeekOrigin.Begin);
			using (var sr = new StreamReader (ms, Encoding.UTF8, true, 1024, leaveOpen: true)) {
				sr.Peek ();
				encoding = sr.CurrentEncoding;
				fullText = sr.ReadToEnd ();
			}
			return CheckHashInternalMultipleLineEndings (hashAlgorithm, fullText, encoding, transformedHash);
		}

		static bool CheckHashInternalMultipleLineEndings (HashAlgorithm algorithm, string fullFileText,
			Encoding fileEncoding, byte[] expectedHash)
		{
			foreach (var lineEndingReplacement in LineEndingReplacements) {
				if (CheckHashInternalReplacingLineEndings (algorithm, fullFileText, fileEncoding, expectedHash,
					lineEndingReplacement))
					return true;
			}
			return false;
		}

		static bool CheckHashInternalReplacingLineEndings (HashAlgorithm algorithm, string fullFileText,
			Encoding fileEncoding, byte[] expectedHash, Tuple<string, string> lineEndingReplacement)
		{
			// replace line endings and then calculate the hash.
			// It may me helpful when sources were compiled on one OS and then checked out on another with different endings
			fullFileText = fullFileText.Replace (lineEndingReplacement.Item1, lineEndingReplacement.Item2);

			var preamble = fileEncoding.GetPreamble ();
			var textBytes = fileEncoding.GetBytes (fullFileText);
			var allBytes = new byte[preamble.Length + textBytes.Length];
			Array.Copy (preamble, allBytes, preamble.Length);
			Array.Copy (textBytes, 0, allBytes, preamble.Length, textBytes.Length);
			return CheckHashForContentArray (algorithm, allBytes, expectedHash);
		}

		static bool CheckHashForContentArray (HashAlgorithm algorithm, byte[] contentArray, byte[] hashFromSymbolFile)
		{
			var computedHash = algorithm.ComputeHash (contentArray);
			return CompareHashes (computedHash, hashFromSymbolFile);
		}

		static bool CheckHashForContentStream (HashAlgorithm algorithm, Stream stream, byte[] hasFromSymbolFile)
		{
			var computedHash = algorithm.ComputeHash (stream);
			return CompareHashes (computedHash, hasFromSymbolFile);
		}

		static bool CompareHashes (byte[] actual, byte[] expected)
		{
			// for SHA1 hash Mono returns only first 15 bytes of it
			var length = Math.Min (actual.Length, expected.Length);
			for (int i = 0; i < length; i++) {
				if (actual[i] != expected[i])
					return false;
			}
			return true;
		}
	}
}