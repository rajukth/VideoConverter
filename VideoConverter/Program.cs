using Microsoft.AspNetCore.Http.Features;
using VideoConverter.Hubs;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;


// Configure FFmpeg binaries path
var ffmpegPath = Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg-binaries");
Directory.CreateDirectory(ffmpegPath); // Ensure the directory exists
await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
FFmpeg.SetExecutablesPath(ffmpegPath);
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 4294967295; // 4 GB
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<ProgressHub>("/progressHub");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    endpoints.MapHub<VideoConverterHub>("/videoConverterHub");
});

app.Run();
