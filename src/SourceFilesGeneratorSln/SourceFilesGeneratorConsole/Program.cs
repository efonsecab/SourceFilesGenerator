using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using TestGenerateAdminCode;

namespace SourceFilesGeneratorConsole
{
    class Program
    {
        private static string modelsDestinationFolder;
        private static string blazorFilesDestinationFolder;
        private static string apiControllersDestinationFolder;
        private static string sourceAssembliesDirectory;
        private static string sourceDataAccessAssemblyName;
        private static string sourceEntitiesNamespace;
        private static string basePagesRoute;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build(); ;
            modelsDestinationFolder = config["ModelsDestinationFolder"];
            blazorFilesDestinationFolder = config["BlazorFilesDestinationFolder"];
            apiControllersDestinationFolder = config["ApiControllersDestinationFolder"];
            sourceAssembliesDirectory = config["SourceAssembliesDirectory"];
            sourceDataAccessAssemblyName = config["SourceDataAccessAssemblyName"];
            sourceEntitiesNamespace = config["SourceEntitiesNamespace"];
            basePagesRoute = config["BasePagesRoute"];
            SourceFilesGenerator sourceFilesGenerator = new SourceFilesGenerator();
            sourceFilesGenerator.GenerateFiles(modelsDestinationFolder, blazorFilesDestinationFolder, 
                sourceAssembliesDirectory,
                sourceDataAccessAssemblyName, apiControllersDestinationFolder, 
                sourceEntitiesNamespace, basePagesRoute);
        }
    }
}
