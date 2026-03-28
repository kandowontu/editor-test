using System;
using System.IO;
Console.SetError(TextWriter.Null);
Console.Error.WriteLine(""THIS SHOULD NOT APPEAR ON STDERR"");
Console.WriteLine(""STDOUT OK"");
