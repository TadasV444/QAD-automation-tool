using QadAutomation.Cli;

// The entry point stays this thin on purpose: every decision worth testing lives
// in CommandLineApplication, which takes its output streams as arguments and can
// therefore be exercised end-to-end from a unit test.
return new CommandLineApplication(Console.Out, Console.Error).Run(args);
