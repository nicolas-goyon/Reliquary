using System.CommandLine;
using Reliquary.Cli;

var root = new RootCommand("The command line tool of Reliquary for the administrators of an instance.");
root.Subcommands.Add(MigrateCommand.Create());
root.Subcommands.Add(JobsCommand.Create());
root.Subcommands.Add(DemoCommand.Create());
root.Subcommands.Add(BackupCommand.CreateBackup());
root.Subcommands.Add(BackupCommand.CreateRestore());

return await root.Parse(args).InvokeAsync();
