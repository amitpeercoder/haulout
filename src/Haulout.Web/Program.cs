using Haulout.Web;
using Haulout.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// One game per browser tab. The session is the only thing that touches the sim facade.
builder.Services.AddSingleton<GameSession>();

await builder.Build().RunAsync();
