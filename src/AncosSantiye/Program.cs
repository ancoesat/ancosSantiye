using AncosSantiye;

if (!Cli.IsServerStart(args))
{
    return await Cli.Run(args, Console.In, Console.Out, Console.Error);
}

var app = SiteApp.Build(args);
app.Run();
return 0;
