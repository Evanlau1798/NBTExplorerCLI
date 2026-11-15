using Next.Cli;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);
return CliApplication.Run(args, Console.Out);
