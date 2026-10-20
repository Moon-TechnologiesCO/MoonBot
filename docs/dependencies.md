# Dependencias y licencias

Regla del proyecto: ninguna dependencia entra sin revisar licencia, uso comercial, atribución y obligaciones de distribución.

Revisión del 20 de octubre de 2026. Fuente: el campo `<license>` del `.nuspec` de cada paquete en la caché local de NuGet (`~/.nuget/packages`).

## Se distribuyen con MoonBot

| Paquete | Versión | Licencia | Uso comercial | Obligación |
| --- | --- | --- | --- | --- |
| Avalonia | 12.1.3 | MIT | Sí | Conservar el aviso de copyright y la licencia |
| Avalonia.Desktop | 12.1.3 | MIT | Sí | Igual |
| Avalonia.Themes.Fluent | 12.1.3 | MIT | Sí | Igual |
| Avalonia.Fonts.Inter | 12.1.3 | MIT (paquete) | Sí | Ver nota sobre la fuente |
| SkiaSharp (transitiva) | 3.119.4 | MIT | Sí | Igual |
| HarfBuzzSharp (transitiva) | 8.3.1.3 | MIT | Sí | Igual |
| MicroCom.Runtime (transitiva) | 0.11.6 | MIT | Sí | Igual |

**Nota sobre Inter:** el paquete declara MIT, pero la tipografía Inter se publica originalmente bajo la SIL Open Font License 1.1. La OFL permite uso comercial y embebido; prohíbe vender la fuente sola. Antes del primer release hay que confirmar qué licencia acompaña a los archivos de fuente dentro del paquete.

## Solo para desarrollo (no se distribuyen)

| Paquete | Versión | Licencia | Nota |
| --- | --- | --- | --- |
| AvaloniaUI.DiagnosticsSupport | 2.2.3 | **No declarada** | © AvaloniaUI OÜ. Sin licencia explícita, se asume "todos los derechos reservados". Solo entra en Debug (`PrivateAssets=All` fuera de Debug). Revisar sus términos o quitarlo antes de publicar binarios Debug. |
| xunit | 2.9.3 | Apache-2.0 | Tests |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | Tests |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | Tests |
| coverlet.collector | 6.0.4 | MIT | Tests |

## Decisión: ningún paquete nuevo en la v0.2

El chat con IA, el JSON, los logs y el Credential Manager usan solo lo que trae .NET (`HttpClient`, `System.Text.Json`, P/Invoke a `advapi32.dll`). Menos dependencias significa menos superficie de ataque en la cadena de suministro y menos licencias que vigilar.

## Pendiente

- Archivo `THIRD-PARTY-NOTICES.md` con los textos MIT y OFL antes del primer instalador.
- Revisar la licencia del SDK oficial de Anthropic antes de añadir ese proveedor (etapa 4).
