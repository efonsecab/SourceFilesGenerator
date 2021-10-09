using System;
using System.IO;
using TestGenerateAdminCode;

namespace SourceFilesGeneratorConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            string modelsDestFolder = @"";
            string blazorFilesDestFolder = @"";
            string assembliesDirectory = @"";
            string dataAccessAssemblyName = @"";

            SourceFilesGenerator sourceFilesGenerator = new SourceFilesGenerator();
            sourceFilesGenerator.GenerateFiles(modelsDestFolder, blazorFilesDestFolder, assembliesDirectory,
                dataAccessAssemblyName);
        }
    }
}
