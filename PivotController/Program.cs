using PivotController.Data;

var builder = WebApplication.CreateBuilder(args);
var CustomOrigins = "_customOrigins";
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CustomOrigins,
    builder =>
    {
        builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});
builder.Services.AddMemoryCache((options) =>
{
    options.SizeLimit = 100;
});
builder.Services.AddScoped(serviceProvider =>
{
    IWebHostEnvironment environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new DynamicDataLoader(configuration, Path.Combine(environment.ContentRootPath, "DataSource"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(CustomOrigins);
app.UseExceptionHandler();
app.UseAuthorization();
app.MapControllers();
app.Run();
