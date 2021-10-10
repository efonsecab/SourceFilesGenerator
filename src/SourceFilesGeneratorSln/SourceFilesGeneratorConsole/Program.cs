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
        

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build(); ;
            SourceFilesGeneratorConfiguration sourceFilesGeneratorConfiguration = config
                .GetSection("SourceFilesGeneratorConfiguration").Get<SourceFilesGeneratorConfiguration>();
            SourceFilesGenerator sourceFilesGenerator = new SourceFilesGenerator(sourceFilesGeneratorConfiguration);
            sourceFilesGenerator.GenerateFiles();
        }
    }
}
