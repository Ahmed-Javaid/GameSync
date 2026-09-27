// GameSync's background app (design.md → Background work), with no window. With no command it watches your games and
// syncs each after you play; "daily" is the daily backup; any other command runs as the command line would, like
// "launch <game> -- %command%" from Steam's launch options.
return await GameSync.Host.Background.RunAsync(args, GameSync.Tray.Toasts.Show);
