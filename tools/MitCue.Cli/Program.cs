using System.Text;
using MitCue.Cli;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
return Cli.Run(args, Console.Out, Console.Error, CliEnvironment.FromProcess());
