using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using qlthucung;
using qlthucung.Helpers;
using qlthucung.Models;
using qlthucung.Security;
using qlthucung.Services.chat;
using qlthucung.Services.email;
using qlthucung.Services.vnpay;
using System;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// ==========================
// LISTEN PORT (DOCKER)
// ==========================
builder.WebHost.UseUrls("http://0.0.0.0:80");

// ==========================
// DATABASE
// ==========================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AppDb")));

builder.Services.AddDbContext<AppIdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AppDb")));

// ==========================
// IDENTITY
// ==========================
builder.Services.AddIdentity<AppIdentityUser, AppIdentityRole>()
    .AddRoles<AppIdentityRole>()
    .AddEntityFrameworkStores<AppIdentityDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Security/SignIn";
    options.AccessDeniedPath = "/Security/AccessDenied";
});

builder.Services.Configure<IdentityOptions>(options =>
{
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
});

// ==========================
// REGISTER TF-IDF INDEXER (SCOPED)
// ==========================
// TfidfIndexer depends on AppDbContext (scoped) and a path string
builder.Services.AddScoped<TfidfIndexer>(sp =>
{
    var ctx = sp.GetRequiredService<AppDbContext>();
    var indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tfidf_index.json");
    return new TfidfIndexer(indexPath, ctx);
});

// Hosted initializer to load/build index asynchronously at startup
builder.Services.AddHostedService<TfidfIndexInitializer>();

// ==========================
// SERVICES
// ==========================
builder.Services.AddScoped<IVnPayService, VnPayService>();
builder.Services.AddSingleton<IEmbeddingService, OpenAIEmbeddingService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IViewRenderService, ViewRenderService>();

// ==========================
// EMAIL
// ==========================
builder.Services.Configure<SmtpSettings>(
    builder.Configuration.GetSection("SmtpSettings"));

builder.Services.AddTransient<IEmailSender, EmailSender>();

// ==========================
// MVC + SIGNALR
// ==========================
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// ==========================
// SESSION + HTTP
// ==========================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();

// ==========================
// SIGNALR USER ID
// ==========================
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();

// ==========================
// BUILD APP
// ==========================
var app = builder.Build();

// ==========================
// MIDDLEWARE
// ==========================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// ==========================
// ENDPOINTS
// ==========================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<ChatHub>("/ChatHub");

// ==========================
// RUN
// ==========================
app.Run();
