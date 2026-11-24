using AncosSantiye;

if (!Cli.IsServerStart(args))
{
    return await Cli.Run(args, Console.In, Console.Out, Console.Error);
}

WebApplication app;
try
{
    app = SiteApp.Build(args);
}
catch (SiteApp.SetupException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

app.Run();
return 0;
