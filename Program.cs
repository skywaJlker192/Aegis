using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Aegis.Authorization;
using Aegis.Data;
using Aegis.Hubs;
using Aegis.Models;
using Aegis.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================
// 1. БАЗА ДАННЫХ (EF Core + SQLite)
// =====================================================================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// =====================================================================
// 2. IDENTITY: пользователи, роли, пароли, блокировка
// =====================================================================
builder.Services
    .AddIdentity<Agent, IdentityRole>(options =>
    {
        // Пароль: минимум 6 символов и обязательная цифра
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;

        // Блокировка: 5 неудачных попыток -> 15 минут
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Cookie-схема для Razor Pages. Для AJAX и API вместо редиректа на форму входа отдаём 401/403.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Events.OnRedirectToLogin = context =>
    {
        if (IsNonPageRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (IsNonPageRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// =====================================================================
// 3. JWT-схема для REST API (рядом с cookie-схемой Identity)
// =====================================================================
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
                  ?? throw new InvalidOperationException("В appsettings.json нет раздела Jwt.");

if (jwtSettings.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key должен быть не короче 32 символов.");
}

builder.Services
    .AddAuthentication() // схемы по умолчанию уже заданы Identity (cookie); JWT добавляем как вторую
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
            ClockSkew = TimeSpan.Zero // токен живёт ровно столько, сколько указано
        };
    });

// =====================================================================
// 4. АВТОРИЗАЦИЯ: resource-based политика
// =====================================================================
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanAbortExpedition", policy =>
        policy.AddRequirements(new CanAbortExpeditionRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, CanAbortExpeditionHandler>();

// =====================================================================
// 5. CSRF: токен читаем из заголовка X-CSRF-TOKEN (его кладёт AJAX из <meta name="csrf-token">)
// =====================================================================
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// =====================================================================
// 6. MVC: Razor Pages + контроллеры (API и AJAX)
// =====================================================================
builder.Services
    .AddRazorPages()
    .AddMvcOptions(options =>
    {
        // Глобальная защита от CSRF: проверяются все небезопасные методы (POST/PUT/DELETE).
        // JWT-контроллеры API помечены [IgnoreAntiforgeryToken] — у них нет cookie, CSRF невозможен.
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    });

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enum'ы в JSON — строками ("Critical"), а не числами
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// =====================================================================
// 7. ВЕРСИОНИРОВАНИЕ API (/api/v1/..., /api/v2/...) и SWAGGER
// =====================================================================
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";      // группы: v1, v2
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AEGIS API",
        Version = "v1",
        Description = "Мониторинг аномалий. Версия 1: базовые поля аномалии."
    });
    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Title = "AEGIS API",
        Version = "v2",
        Description = "Версия 2: к аномалии добавлен массив TelemetryLogs (последние замеры)."
    });

    // Кнопка «Authorize» для ввода Bearer-токена
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Получите токен через POST /api/auth/login и вставьте его сюда (слово Bearer писать не нужно)."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // Описания эндпоинтов берём из XML-комментариев в коде
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// =====================================================================
// 8. SIGNALR и сервисы приложения
// =====================================================================
builder.Services.AddSignalR();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<IAnomalyService, AnomalyService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IScanService, ScanService>(); // мини-игра «Протокол сканирования»

// «Живой трекер агентов»: положения в памяти + фоновая служба, которая раз в 30 с шлёт AgentLocationUpdate
builder.Services.AddSingleton<IAgentTracker, AgentTracker>();
builder.Services.AddHostedService<AgentTelemetryWorker>();

var app = builder.Build();

// =====================================================================
// Миграции + сидирование данных при старте
// =====================================================================
using (var scope = app.Services.CreateScope())
{
    await DbSeeder.InitializeAsync(scope.ServiceProvider);
}

// =====================================================================
// Конвейер запросов
// =====================================================================
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
}

// Заголовки безопасности + Content-Security-Policy
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    if (context.Request.Path.StartsWithSegments("/swagger"))
    {
        // Swagger UI использует встроенные скрипты и стили, поэтому для него политика мягче
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:";
    }
    else
    {
        // Для самого приложения: ни одного встроенного скрипта или стиля, только файлы с нашего домена
        headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
            "connect-src 'self' ws: wss:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    }

    await next();
});

app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "AEGIS API v1");
    options.SwaggerEndpoint("/swagger/v2/swagger.json", "AEGIS API v2");
    options.DocumentTitle = "AEGIS API";
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapHub<SituationRoomHub>("/hubs/situation");
app.MapHub<ExpeditionCommsHub>("/hubs/comms"); // чат «Протокол связи»

app.Run();

// AJAX- и API-запросы не должны получать редирект на HTML-страницу входа — им нужен статус 401/403
static bool IsNonPageRequest(HttpRequest request) =>
    request.Path.StartsWithSegments("/ajax") ||
    request.Path.StartsWithSegments("/api") ||
    request.Path.StartsWithSegments("/hubs") ||
    request.Headers["X-Requested-With"].ToString() == "XMLHttpRequest";
