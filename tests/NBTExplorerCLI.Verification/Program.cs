using NBTExplorerCLI.Verification;
using System.Text;

Console.OutputEncoding = new UTF8Encoding(false);
return await VerificationRunner.RunAsync(args);
