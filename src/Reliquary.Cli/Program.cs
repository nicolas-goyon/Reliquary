using System.CommandLine;
using Reliquary.Cli;

var root = new RootCommand("The command line tool of Reliquary for the administrators of an instance.");
root.Subcommands.Add(MigrateCommand.Create());
root.Subcommands.Add(JobsCommand.Create());
root.Subcommands.Add(DemoCommand.Create());

return await root.Parse(args).InvokeAsync();
