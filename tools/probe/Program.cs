using System.Reflection;
var dir = @"E:/260523/py/Glimpseon/app-1.0.0";
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var name = new AssemblyName(e.Name).Name + ".dll";
    var p = Path.Combine(dir, name);
    return File.Exists(p) ? Assembly.LoadFrom(p) : null;
};
var asm = Assembly.LoadFrom(Path.Combine(dir, "ColorTextBlock.Avalonia.dll"));
foreach (var tn in new[] { "CTextBlock", "CCode", "CHyperlink" })
{
    var t = asm.GetTypes().FirstOrDefault(x => x.Name == tn);
    Console.WriteLine($"== {tn} base={t?.BaseType?.Name}");
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name))
        Console.WriteLine($"   {p.Name} : {p.PropertyType.Name}");
}
