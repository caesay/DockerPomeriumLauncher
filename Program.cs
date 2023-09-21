using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dockerSock = Environment.GetEnvironmentVariable("DL_DOCKER");
var port = Environment.GetEnvironmentVariable("DL_PORT");
var pomConfig = Environment.GetEnvironmentVariable("DL_POMERIUM");
var hideContainers = Environment.GetEnvironmentVariable("DL_HIDE");
var customContainers = Environment.GetEnvironmentVariable("DL_EXTRA");
var pageTitle = Environment.GetEnvironmentVariable("DL_TITLE");
var editConfigUrl = Environment.GetEnvironmentVariable("DL_EDIT_CONFIG_URL");
var editContainerUrl = Environment.GetEnvironmentVariable("DL_CONFIGURE_CONTAINER_URL");
var launchNewWindow = Environment.GetEnvironmentVariable("DL_LAUNCH_NEWWINDOW");

var deserializer = new DeserializerBuilder()
    .IgnoreUnmatchedProperties()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .Build();

Dictionary<string, string> _networkCache = new();
DateTime _networkLastClearUtc = DateTime.UtcNow;
DockerClient dockerClient = null;

const string PAGE_INDEX = "page_index.js";
const string PAGE_LAUNCH = "page_launch.js";
const string PAGE_AJAX = "page_ajax.js";

DockerClient GetDockerClient()
{
    dockerClient ??= new DockerClientConfiguration(new Uri(dockerSock)).CreateClient();
    return dockerClient;
}

if (!String.IsNullOrEmpty(port))
{
    app.Urls.Add("http://*:" + port);
}

IResult Page(string jspath, object jsonData = null)
{
    string json = "null";

    if (jsonData != null)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        json = JsonSerializer.Serialize(jsonData, options);
    }

    var resp = $$"""
    <html>
    <head>
        <script src="https://ajax.googleapis.com/ajax/libs/webfont/1.6.26/webfont.js"></script>
        <script>
          WebFont.load({
            google: {
              families: ['Titillium Web:300,600']
            }
          });
        </script>
    
        <link rel="stylesheet" type="text/css" href="/style.css">

        <link rel="icon" type="image/png" sizes="32x32" href="/launchbox-32.png">
        <link rel="icon" type="image/png" sizes="16x16" href="/launchbox-16.png">
        {{(pageTitle == null ? "" : $"<title>{pageTitle}</title>")}}
        <meta name="viewport" content="width=device-width, initial-scale=1">

        <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.2/css/all.min.css" integrity="sha512-z3gLpd7yknf1YoNbCzqRKc4qyor8gaKU1qmn+CShxbuBusANI9QpRohGBreCFkKxLhei6S9CQXFEbbKuqLg0DA==" crossorigin="anonymous" referrerpolicy="no-referrer" />
        <script src="https://cdnjs.cloudflare.com/ajax/libs/lodash.js/4.17.21/lodash.min.js" integrity="sha512-WFN04846sdKMIP5LKNphMaWzU7YpMyCU245etK3g/2ARYbPK9Ub18eG+ljU96qKRCWh+quCY7yefSmlkQw1ANQ==" crossorigin="anonymous" referrerpolicy="no-referrer"></script>
    </head>
    <body>
        <script> window.myData = {{json}}; </script>
        <script type="module" src="/{{jspath}}"></script>
    </body>
    </html>
    """;

    return Results.Text(resp, "text/html");
}

app.MapGet("/{fileName:regex(^[\\d\\w-]+(\\.js|\\.css|\\.png)$)}", (string fileName) =>
{
    byte[] bytes = null;
#if DEBUG
    if (Debugger.IsAttached)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", fileName);
        bytes = File.ReadAllBytes(path);
    }
    else
    {
#endif
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "DockerLauncher." + fileName;
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        using (var memoryStream = new MemoryStream())
        {
            stream.CopyTo(memoryStream);
            bytes = memoryStream.ToArray();
        }
#if DEBUG
    }
#endif

    if (fileName.EndsWith(".js"))
    {
        return Results.File(bytes, "text/javascript");
    }
    else if (fileName.EndsWith(".css"))
    {
        return Results.File(bytes, "text/css");
    }
    else if (fileName.EndsWith(".png"))
    {
        return Results.File(bytes, "image/png", fileName);
    }

    return Results.NotFound();

});

string GetRootHost(HttpContext ctx)
{
    if (Regex.IsMatch(ctx.Request.Host.Value, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}"))
        return ctx.Request.Host.Value;

    var root = Regex.Replace(ctx.Request.Host.Value, "^.+?\\.(.+\\..+$)", "$1");
    return root;
}

app.MapGet("/", async (HttpContext ctx) =>
{
    var containersTask = GetAllContainers(GetRootHost(ctx));
    await Task.WhenAny(Task.Delay(300), containersTask);

    if (containersTask.IsCompletedSuccessfully)
    {
        return Page(PAGE_INDEX, new { launchNewWindow = !String.IsNullOrEmpty(launchNewWindow), preload = containersTask.Result });
    }
    else
    {
        return Page(PAGE_INDEX, new { launchNewWindow = !String.IsNullOrEmpty(launchNewWindow) });
    }
});

app.MapGet("/status", async (HttpContext ctx) =>
{
    return Results.Json(await GetAllContainers(GetRootHost(ctx)));
});

app.MapGet("/status/{containerName}", async (HttpContext ctx, string containerName) =>
{
    var container = await GetContainer(GetRootHost(ctx), containerName, false);
    if (container == null) return Results.NotFound();
    return Results.Json(container);
});

app.MapGet("/start/{containerName}", (string containerName) => Page(PAGE_AJAX, new { message = $"Starting {containerName}...", postUrl = $"/start/{containerName}" }));

app.MapPost("/start/{containerName}", async (HttpContext ctx, string containerName) =>
{
    var container = await GetContainer(GetRootHost(ctx), containerName, false);
    if (container == null) return Results.NotFound();
    await GetDockerClient().Containers.StartContainerAsync(container.Id, new());
    return Results.Redirect("/");
});

app.MapGet("/stop/{containerName}", (string containerName) => Page(PAGE_AJAX, new { message = $"Stopping {containerName}...", postUrl = $"/stop/{containerName}" }));

app.MapPost("/stop/{containerName}", async (HttpContext ctx, string containerName) =>
{
    var container = await GetContainer(GetRootHost(ctx), containerName, false);
    if (container == null) return Results.NotFound();
    await GetDockerClient().Containers.StopContainerAsync(container.Id, new());
    return Results.Redirect("/");
});

app.MapGet("/restart/{containerName}", (string containerName) => Page(PAGE_AJAX, new { message = $"Restarting {containerName}...", postUrl = $"/restart/{containerName}" }));

app.MapPost("/restart/{containerName}", async (HttpContext ctx, string containerName) =>
{
    var container = await GetContainer(GetRootHost(ctx), containerName, false);
    if (container == null) return Results.NotFound();
    await GetDockerClient().Containers.RestartContainerAsync(container.Id, new());
    return Results.Redirect("/");
});

app.MapGet("/launch/{did}", async (HttpContext ctx) =>
{
    var dockerName = ctx.Request.RouteValues["did"] as string;
    var container = await GetContainer(GetRootHost(ctx), dockerName, false);

    if (container == null)
    {
        return Results.Text("Container not found", statusCode: 404);
    }

    if (String.IsNullOrWhiteSpace(container.NavigateUrl))
    {
        return Results.Text("Container has no navigation target", statusCode: 412);
    }

    switch (container.State)
    {
        case "created":
        case "exited":
            await GetDockerClient().Containers.StartContainerAsync(container.Id, new());
            return Page(PAGE_LAUNCH, new { containerName = container.Name, navigateUrl = container.NavigateUrl });

        case "paused":
            await GetDockerClient().Containers.UnpauseContainerAsync(container.Id, new());
            return Page(PAGE_LAUNCH, new { containerName = container.Name, navigateUrl = container.NavigateUrl });

        case "restarting":
            return Page(PAGE_LAUNCH, new { containerName = container.Name, navigateUrl = container.NavigateUrl });

        case "running":
            return Results.Redirect(container.NavigateUrl);

        default:
            return Results.Text($"Unhandled container state: '{container.State}'", statusCode: 500);
    }
});

app.Run();

async Task<string> GetDockerSubnet(DockerClient client, string netName)
{
    // clear cache every 10 minutes
    var delta = DateTime.UtcNow - _networkLastClearUtc;
    if (delta > TimeSpan.FromMinutes(10))
        _networkLastClearUtc = new();

    if (_networkCache.TryGetValue(netName, out var nv))
        return nv;

    var networks = await client.Networks.ListNetworksAsync(new NetworksListParameters());

    var search = networks.FirstOrDefault(n => n.Name == netName);
    if (search == null)
    {
        _networkCache[netName] = null;
        return null;
    }

    var insp = await client.Networks.InspectNetworkAsync(search.ID);

    var subnet = insp?.IPAM?.Config?.FirstOrDefault();
    if (subnet?.Subnet == null)
    {
        _networkCache[netName] = null;
        return null;
    }

    _networkCache[netName] = subnet.Subnet;
    return subnet.Subnet;
}

async Task<ContainerItem[]> MapContainerResponse(string rootHost, DockerClient client, IList<ContainerListResponse> ca, bool launchRoutes = true)
{
    var p = deserializer.Deserialize<PomeriumRoot>(File.ReadAllText(pomConfig));
    // var routes = p.Policy
    //     .Where(z => z.To != null)
    //     .ToDictionary(z => new Uri(z.To).Host, z => z.From, StringComparer.OrdinalIgnoreCase);

    var hidden = String.IsNullOrWhiteSpace(hideContainers)
        ? new string[0]
        : hideContainers.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var query = from c in ca
                let networks = c.NetworkSettings?.Networks?.ToArray() ?? new KeyValuePair<string, EndpointSettings>[0]
                where networks.All(z => !hidden.Contains(z.Key))
                let n = networks.FirstOrDefault()
                let name = c.Names.First().TrimStart('/')
                let croute = p.Policy.Where(z => z.To != null).FirstOrDefault(pr => new Uri(pr.To).Host == name)?.From
                where !hidden.Contains(name)
                select new ContainerItem
                {
                    Name = name,
                    State = c.State,
                    Id = c.ID,
                    IconUrl = c.Labels.Where(l => l.Key.Equals("net.unraid.docker.icon")).Select(l => l.Value).FirstOrDefault(),
                    NetworkName = n.Key,
                    IpAddress = n.Value?.IPAddress,
                    NavigateUrl = croute != null ? (launchRoutes ? $"/launch/{name}" : croute) : null,
                    Running = c.State == "running",
                    Mounts = c.Mounts?.Select(z => z.Source).Where(z => !String.IsNullOrWhiteSpace(z)).ToArray() ?? new string[0],
                    Ports = c.Ports.DistinctBy(p => p.PrivatePort).DistinctBy(p => p.PublicPort).OrderBy(p => p.PrivatePort).ToArray(),
                    ExtraActions = new(),
                };

    var computed = query.ToArray();

    if (editConfigUrl != null)
    {
        if (!editConfigUrl.Contains(":") || !editConfigUrl.Contains("{path}"))
            throw new Exception("Invalid format for DL_EDIT_URL_TEMPLATE");
        var spl = editConfigUrl.Split(':');
        if (spl.Length < 2)
            throw new Exception("Invalid format for DL_EDIT_URL_TEMPLATE");
        var pathPrefix = spl[0];
        var substitution = String.Join(":", spl.Skip(1));

        foreach (var c in computed)
        {
            var m = (c.Mounts?.FirstOrDefault(m => m.StartsWith("/mnt/user/appdata/" + c.Name))
                    ?? c.Mounts.FirstOrDefault(m => m.StartsWith("/mnt/user/appdata/") && m.Contains(c.Name)));

            if (m != null && m.StartsWith(pathPrefix))
            {
                m = m.Substring(pathPrefix.Length);

                var mParts = m.Split("/");
                var mAppIndex = Array.IndexOf(mParts, "appdata");

                m = String.Join("/", mParts.Take(mAppIndex + 2));


                var url = substitution.Replace("{path}", m);
                c.ExtraActions["\uf013 Edit AppData"] = url;
            }
        }
    }

    if (editContainerUrl != null)
    {
        if (!editContainerUrl.Contains("{name}"))
            throw new Exception("Invalid format for DL_CONFIGURE_CONTAINER_URL");

        foreach (var c in computed)
        {
            c.ExtraActions["\uf1b2 Docker Template"] = editContainerUrl.Replace("{name}", c.Name);
        }
    }

    ContainerItem[] customs = new ContainerItem[0];

    if (!String.IsNullOrWhiteSpace(customContainers))
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
        };
        customs = JsonSerializer.Deserialize<ContainerItem[]>(customContainers.Trim(), options);
    }

    if (customs.Any())
    {
        var dict = computed.ToDictionary(q => q.Name, q => q);
        foreach (var c in customs)
        {
            if (dict.ContainsKey(c.Name))
            {
                var dockerObj = dict[c.Name];
                var properties = typeof(ContainerItem).GetProperties();
                foreach (var prop in properties)
                {
                    var cVal = prop.GetValue(c);
                    if (cVal != null)
                    {
                        prop.SetValue(dockerObj, cVal);
                    }
                }
            }
            else
            {
                dict.Add(c.Name, c);
            }
        }

        computed = dict.Values.ToArray();
    }

    foreach (var c in computed)
    {
        if (c.NetworkName != null)
        {
            var subnet = await GetDockerSubnet(client, c.NetworkName);
            if (subnet != null)
            {
                //c.NetworkName = $"{c.NetworkName} ({subnet})";
                c.NetworkName = $"{subnet} - {c.NetworkName}";
            }
        }

        if (c.NavigateUrl != null)
        {
            if (c.NavigateUrl.EndsWith(".*"))
            {
                c.NavigateUrl = Regex.Replace(c.NavigateUrl, @"\.\*$", "." + rootHost);
            }

            if (c.NavigateUrl.Contains("*"))
            {
                c.NavigateUrl = null;
            }
        }
    }

    return computed.OrderBy(c => c.Name).ToArray();
}

async Task<ContainerItem> GetContainer(string rootHost, string name, bool launchRoutes = true)
{
    var req = new ContainersListParameters
    {
        All = true,
        Filters = new Dictionary<string, IDictionary<string, bool>>
        {
            {"name", new Dictionary<string, bool>
                {
                    {name, true}
                }
            }
        }
    };

    var client = GetDockerClient();
    var ca = await client.Containers.ListContainersAsync(req);
    if (!ca.Any())
    {
        return null;
    }

    return (await MapContainerResponse(rootHost, client, ca, launchRoutes)).First(c => c.Name == name);
}

async Task<ContainerItem[]> GetAllContainers(string rootHost, bool launchRoutes = true)
{
    var client = GetDockerClient();
    var ca = await client.Containers.ListContainersAsync(new ContainersListParameters { All = true, Limit = 1000 });
    if (!ca.Any())
    {
        return new ContainerItem[0];
    }
    return await MapContainerResponse(rootHost, client, ca, launchRoutes);
}

record ContainerItem
{
    public string Name { get; set; }
    public string State { get; set; }
    public bool? Running { get; set; }
    public string Id { get; set; }
    public string IconUrl { get; set; }
    public string NavigateUrl { get; set; }
    public string IpAddress { get; set; }
    public string NetworkName { get; set; }
    public string[] Mounts { get; set; }
    public Port[] Ports { get; set; }
    public Dictionary<string, string> ExtraActions { get; set; }
}

class PomeriumRoute
{
    public string From { get; set; }
    public string To { get; set; }
}

class PomeriumRoot
{
    public PomeriumRoute[] Policy { get; set; }
}