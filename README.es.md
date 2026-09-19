# 🖼️ Wallpaper Control

🌐 **Idioma:** [English](README.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md) · [日本語](README.ja.md)

**Wallpaper Control** es una utilidad ligera para Windows que permite gestionar y mostrar presentaciones de fondos de escritorio con una programación precisa, transiciones animadas, estadísticas, widgets de escritorio nativos y funciones adicionales para facilitar el uso.

Amplía las funciones estándar de fondos de escritorio de Windows con su propio motor de presentación sincronizado con el reloj, efectos de transición renderizados directamente en el escritorio y widgets opcionales. Se integra limpiamente con el escritorio de Windows y restaura la gestión nativa de fondos al cerrar la aplicación.

**Versión actual: v1.8.4**

## ✨ Funciones

- 🖼️ **Control de presentación de fondos**
  - Selecciona tu carpeta de fondos
  - Cambia el intervalo de la presentación
  - Activa o desactiva el orden aleatorio
  - Cambia inmediatamente al siguiente fondo
  - Acción **Siguiente fondo** resaltada para facilitar el acceso
  - Sección dedicada al fondo actual con acciones rápidas e información emergente de la ruta completa
  - Pausa y reanuda la presentación
  - Pausa automática mientras haya aplicaciones a pantalla completa, también en monitores adicionales
  - Un retraso de dos segundos evita que un breve Alt-Tab reactive inmediatamente la actividad en segundo plano
  - Las pausas manuales de la presentación se conservan de forma independiente
  - Fija el fondo actual

- 🎬 **Efectos de transición**
  - Transiciones suaves renderizadas directamente en el escritorio de Windows
  - Wipe con dirección seleccionable: izquierda, derecha, arriba, abajo o aleatoria
  - Slide con dirección seleccionable: izquierda, derecha, arriba, abajo o aleatoria
  - Fade
  - Zoom con variantes de acercamiento y alejamiento
  - Split
  - Curtain
  - El modo aleatorio elige un efecto diferente en cada cambio y también varía la dirección o el modo de zoom cuando corresponde
  - Duración de transición configurable
  - Los iconos y herramientas del escritorio permanecen visibles sobre la capa de transición

- 🕐 **Widgets de escritorio nativos**
  - Widget de reloj con 5 temas seleccionables
  - Widget de monitorización del sistema
  - Widget del tiempo con previsión opcional de 3 días
  - Widget de calendario compatible con iCalendar / ICS
  - Botón opcional Siguiente fondo
  - Los widgets pueden colocarse independientemente en cualquier parte del escritorio
  - Bloqueo independiente de posición para cada widget
  - Se recuerdan las posiciones y configuraciones de los widgets
  - Vista previa en directo al cambiar la configuración
  - Los widgets forman parte del escritorio y no permanecen sobre las ventanas normales

- 🖥️ **Integración con Windows**
  - Se integra con las API nativas de fondos de Windows y aporta su propio motor de temporización y transiciones
  - Temporización personalizada sincronizada con el reloj para cambios precisos
  - Los cambios manuales no reinician la programación automática
  - Admite diferentes modos de visualización
  - Detecta cambios externos del fondo
  - Abre carpetas con el gestor de archivos predeterminado configurado
  - Inicio automático opcional con Windows
  - Restaura la gestión nativa de fondos de Windows al cerrar Wallpaper Control
  - Se ejecuta como una única instancia por usuario de Windows
  - Volver a iniciar Wallpaper Control trae la ventana existente al primer plano
  - Permite cambiar externamente el fondo mediante el argumento `--next`
  - Los comandos `--next` se envían de forma segura a la instancia en ejecución del usuario actual
  - Expone el estado del widget de reloj nativo para aplicaciones y scripts externos

- 📊 **Panel de estadísticas**
  - Estadísticas persistentes de los fondos incluso después de reiniciar la aplicación
  - Registra las visualizaciones y cuándo se mostró por última vez cada fondo
  - Estadísticas por periodo: hoy, ayer, últimos 7 días y últimos 30 días
  - Vistas de Top 10, Top 25 y estadísticas completas
  - Métricas del panel para los fondos más vistos, menos vistos y el promedio de visualizaciones
  - Uniformidad de distribución metric
  - Gráfico Top 10 de fondos
  - Tiempo medio de reaparición de los fondos
  - Análisis de fondos poco mostrados
  - Detecta fondos que nunca se han mostrado
  - Búsqueda y columnas ordenables
  - Miniaturas de los fondos y vista previa al pasar el cursor
  - Establece un fondo directamente desde la ventana de estadísticas
  - Abre los fondos o sus carpetas desde el menú contextual
  - Elimina entradas individuales o restablece todas las estadísticas
  - Copia de seguridad y recuperación automáticas si no puede cargarse el archivo principal de estadísticas
  - Los archivos de estadísticas dañados se conservan para una posible recuperación

- 🗑️ **Descarte rápido de fondos**
  - Mueve los fondos no deseados a una carpeta `Aussortiert` con un solo clic
  - El descarte de fondos se desactiva temporalmente mientras se ejecuta una transición
  - El siguiente fondo se muestra por completo antes de mover el fondo descartado
  - Carpeta global de descartes opcional
  - Subcarpetas opcionales para colecciones individuales de fondos
  - Deshace el último descarte

- 📜 **Historial de fondos**
  - Registra los fondos mostrados recientemente durante la sesión actual
  - Abre los fondos directamente en el visor de imágenes predeterminado
  - Vista previa al pasar el cursor para identificarlos rápidamente

- ⌨️ **Atajos de teclado globales**
  - Siguiente fondo
  - Pausar / Reanudar
  - Mostrar el fondo actual en el gestor de archivos
  - Descartar el fondo actual
  - Los atajos pueden personalizarse o desactivarse
  - Detecta asignaciones de atajos duplicadas
  - Avisa cuando Windows no puede registrar un atajo seleccionado
  - Los atajos pueden intercambiarse entre acciones sin conflictos con asignaciones anteriores
  - Los atajos no modificados permanecen registrados cuando se cambian otros
  - Atajo predeterminado para descartar: `Ctrl+Alt+Shift+R`

- 🔔 **Compatibilidad con el área de notificación**
  - Wallpaper Control puede seguir ejecutándose en el área de notificación
  - Haz doble clic en el icono del área de notificación para restaurar la ventana
  - Comportamiento opcional de **cerrar al área de notificación** al pulsar el botón X de la ventana
  - Cierra la aplicación directamente desde el menú del área de notificación

- 🎨 **Interfaz y apariencia**
  - Interfaz de Configuración rediseñada
  - Aplicación principal rediseñada para coincidir con la interfaz de Configuración
  - Apariencia moderna y coherente en toda la aplicación
  - Selección de temas Sistema, Oscuro y Claro
  - El tema Sistema sigue automáticamente el tema de aplicaciones de Windows
  - Opacidad de la ventana ajustable
  - Recuerda la posición de la ventana
  - Compatible con arrastrar y soltar
  - Interfaz de Configuración reorganizada
  - Configuración siempre se abre en la pestaña **General**
  - Ventana principal renovada con agrupación más clara y una jerarquía visual mejorada
  - Listas desplegables oscuras y mejor legibilidad de los controles desactivados
  - Orden de tabulación mejorado y espaciado uniforme
  - Restablecimiento independiente de la apariencia
  - Interfaz localizada

## 🎮 Pausa a pantalla completa

Wallpaper Control puede reducir automáticamente la actividad en segundo plano mientras haya una aplicación a pantalla completa.

- Detecta aplicaciones a pantalla completa en todos los monitores conectados
- Pausa los cambios automáticos y las animaciones de transición
- Suspende las actualizaciones periódicas de los widgets
- Posponer las comprobaciones automáticas de actualizaciones
- Reanuda la actividad tras dos segundos sin ninguna aplicación a pantalla completa
- Por ello, un breve Alt-Tab no reactiva inmediatamente la actividad pausada
- Una presentación pausada manualmente permanece pausada al finalizar el modo de pantalla completa
- La detección de pantalla completa está activada de forma predeterminada y puede desactivarse en Configuración

## 🔄 Comprobación de actualizaciones

Wallpaper Control puede consultar GitHub Releases para detectar nuevas versiones sin quitar al usuario el control sobre las actualizaciones.

- Comprobación manual disponible en Configuración
- Comprobación automática opcional cada vez que se inicia Wallpaper Control
- Mientras la aplicación siga abierta, las comprobaciones automáticas se repiten cada 24 horas
- Diálogos específicos muestran la versión instalada y la última disponible
- La página de la versión puede abrirse directamente cuando hay una actualización
- Las comprobaciones automáticas pueden desactivarse en Configuración
- Wallpaper Control **nunca descarga ni instala actualizaciones automáticamente**

Si la versión instalada ya está actualizada, las comprobaciones automáticas permanecen silenciosas. Las manuales siempre muestran una respuesta.

## 🕐 Widgets de escritorio

Wallpaper Control incluye widgets nativos que se integran directamente con el escritorio de Windows.

Los widgets pueden colocarse y bloquearse de forma independiente. Sus posiciones y ajustes se conservan entre sesiones.

Los cambios se muestran inmediatamente en una vista previa mientras se configuran. Se aplican permanentemente al guardar. Cancelar restaura el estado y la posición anteriores.

### Reloj

El reloj de escritorio ofrece:

- Visualización de horas y minutos
- Segundos opcionales
- Formato de fecha localizado
- Tamaño ajustable
- 5 temas visuales seleccionables
- Posicionamiento libre
- Bloqueo opcional de posición

El reloj utiliza automáticamente el idioma seleccionado en Wallpaper Control.

### 🖥️ Monitor del sistema

El widget Sistema ofrece directamente en el escritorio un resumen de la información importante del hardware y del sistema.

Puede mostrar:

- Uso de CPU
- Temperatura de CPU
- Uso de RAM
- Uso de GPU
- Temperatura de GPU
- Uso de VRAM
- Actividad de descarga de red
- Actividad de subida de red
- Uso de unidades

El widget incluye barras gráficas compactas y ofrece los estilos **Minimal**, **Clean** y **Glow**.

La monitorización del hardware funciona de forma asíncrona para que las actualizaciones de sensores no interfieran con las transiciones ni con la respuesta de la aplicación.

### 🌦️ Tiempo

El widget Tiempo muestra información meteorológica actual directamente en el escritorio.

Puede mostrar:

- Temperatura actual
- Sensación térmica
- Condiciones meteorológicas actuales
- Humedad
- Precipitación
- Velocidad del viento
- Previsión opcional de 3 días

Los datos meteorológicos los proporciona **Open-Meteo** y no requieren una clave API.

La ubicación puede configurarse en Configuración y la información meteorológica puede actualizarse automáticamente a intervalos seleccionables.

El widget Tiempo ofrece los estilos **Minimal**, **Clean** y **Glow** y puede colocarse y bloquearse de forma independiente.

### 📅 Calendario

El widget Calendario ofrece directamente en el escritorio un resumen compacto de las próximas citas.

Los datos se cargan mediante fuentes **iCalendar / ICS** de solo lectura.

Incluye:

- Compatibilidad con varias fuentes de calendario ICS
- Citas con hora
- Eventos de todo el día
- Eventos recurrentes
- Eventos de varios días
- Los eventos de todo el día que abarcan varios días se muestran en cada día afectado
- Las citas del mismo día se agrupan
- Muestra los próximos días que realmente contienen citas
- Se omiten los días vacíos
- Visualización opcional de la ubicación del evento
- Tamaño automático del widget según las citas mostradas
- Intervalo de actualización configurable
- Estilos **Minimal**, **Clean** y **Glow**
- Posicionamiento y bloqueo independientes

Las direcciones ICS privadas se almacenan cifradas mediante **Windows Data Protection API (DPAPI)** para el usuario actual de Windows.

Las URL de calendarios privados están ocultas de forma predeterminada en la configuración y pueden mostrarse explícitamente para consultarlas o editarlas.

Wallpaper Control solo lee las fuentes del calendario y no modifica sus datos.

#### 🎌 Calendarios de festivos

Se pueden configurar fuentes ICS independientes como calendarios de festivos.

Los festivos se resaltan visualmente y se colocan automáticamente antes de las citas normales del mismo día para facilitar su identificación.

Se pueden combinar varias fuentes de calendarios normales y de festivos en el mismo widget Calendario.

Las actualizaciones del calendario son resistentes a fallos temporales de las fuentes. Los eventos cargados previamente permanecen visibles si una fuente deja de estar disponible, mientras las demás continúan actualizándose. Wallpaper Control indica cuándo los datos en caché pueden estar desactualizados y elimina el aviso cuando todas las fuentes configuradas vuelven a actualizarse correctamente. Los eventos en caché se conservan durante la sesión actual.

### Siguiente fondo

El widget Siguiente fondo ofrece un botón compacto para pasar inmediatamente al siguiente fondo.

Ofrece los estilos **Minimal**, **Clean** y **Glow**. Los cambios se muestran inmediatamente en la vista previa y el estilo seleccionado se recuerda entre sesiones. Las configuraciones existentes siguen usando de forma predeterminada el aspecto **Minimal** anterior.

Puede colocarse y bloquearse independientemente de los demás widgets y utiliza la misma lógica de cambio y transición que la aplicación principal.

## 📊 Estadísticas

Wallpaper Control mantiene estadísticas persistentes sobre los fondos mostrados por la presentación.

El panel de estadísticas puede mostrar:

- Número total de visualizaciones de cada fondo
- Cuándo se mostró por última vez cada fondo
- Proporción de visualizaciones y clasificación de popularidad
- Estadísticas de hoy, ayer, los últimos 7 días y los últimos 30 días
- Fondos más y menos vistos
- Promedio de visualizaciones
- Uniformidad de distribución
- Tiempo medio de reaparición
- Gráfico Top 10
- Fondos nunca mostrados o que llevan mucho tiempo sin aparecer

Las estadísticas se almacenan localmente y se conservan tras reiniciar la aplicación.

Wallpaper Control conserva el archivo de estadísticas anterior como copia de seguridad. Si el archivo principal no puede cargarse, puede recurrir automáticamente a la copia mientras conserva los archivos dañados para una posible recuperación. Las estadísticas se escriben mediante archivos temporales únicos para reducir el riesgo de colisiones o reemplazos incompletos.

Las estadísticas temporales y el seguimiento de recurrencia comienzan cuando se inicializan por primera vez los datos correspondientes. Los datos históricos anteriores no se reconstruyen.

El historial reciente depende de la sesión y se borra al cerrar completamente Wallpaper Control.

## 🗑️ Descartar fondos

¿No te gusta el fondo que aparece actualmente?

Wallpaper Control cambia primero al siguiente fondo y espera a que termine el cambio antes de mover la imagen no deseada a una carpeta `Aussortiert`. El descarte se desactiva temporalmente mientras haya una transición en curso para evitar que la imagen actual se mueva antes de finalizarla.

El destino puede estar dentro de la carpeta actual de fondos o configurarse como una carpeta global de descartes.

¿Has descartado la imagen equivocada por error? El último descarte puede deshacerse durante la sesión actual.

## 🕹️ Control externo

Wallpaper Control puede recibir comandos de aplicaciones externas, scripts, accesos directos o herramientas de escritorio mientras está en ejecución.

### Siguiente fondo

Se admite el siguiente comando:

```text
WallpaperControl.exe --next
```

Esto envía una solicitud a la instancia de Wallpaper Control en ejecución y cambia inmediatamente al siguiente fondo.

Wallpaper Control se ejecuta como una única instancia para el usuario actual de Windows. Si se vuelve a iniciar normalmente, la ventana principal existente pasa al primer plano. La comunicación de comandos como `--next` está restringida al usuario actual y las solicitudes mal formadas, demasiado grandes o bloqueadas se rechazan sin impedir los comandos posteriores.

Las solicitudes externas utilizan la misma lógica de presentación, programación y transición que los cambios iniciados directamente desde Wallpaper Control.

Esto permite integrar Wallpaper Control con scripts personalizados, lanzadores, herramientas de automatización u otras aplicaciones sin abrir la ventana principal.

### Reloj Widget State

Las aplicaciones externas pueden determinar si el reloj nativo de Wallpaper Control está activado leyendo:

```text
HKEY_CURRENT_USER\Software\WallpaperControl
```

Valor del Registro:

```text
ClockWidgetEnabled
```

Valores:

```text
0 = Widget de reloj nativo desactivado
1 = Widget de reloj nativo activado
```

Esto permite que aplicaciones externas o herramientas de escritorio adapten su comportamiento según esté activado o no el reloj nativo.

El valor del Registro representa la configuración guardada del widget. Los cambios de vista previa no se aplican permanentemente hasta guardar la configuración.

## 🩺 Diagnóstico

Wallpaper Control incluye un registro de diagnóstico ligero para errores y fallos inesperados.

Los registros solo se crean cuando son necesarios y se guardan en:

```text
%APPDATA%\WallpaperControl\Logs
```

El archivo de registro principal es:

```text
wallpaper-control.log
```

El registro está pensado para la resolución de problemas y no requiere configuración adicional durante el uso normal.

## 🌍 Idiomas

Wallpaper Control incluye actualmente:

- 🇩🇪 Alemán
- 🇬🇧 Inglés
- 🇫🇷 Francés
- 🇪🇸 Español
- 🇯🇵 Japonés

El idioma de la interfaz puede cambiarse directamente desde la configuración.

Los textos y el formato de fecha de los widgets siguen el idioma seleccionado.

## 💻 Requisitos

- **Windows 11:** compatible y probado
- **Windows 10:** se espera que sea compatible, actualmente no probado
- Windows de 64 bits
- No se requiere una instalación independiente de .NET

## 🚀 Instalación

El **método oficial de instalación** de Wallpaper Control es el instalador Windows x64 incluido con cada versión.

1. Descarga `WallpaperControl-1.8.3-Setup-x64.exe` desde la última versión de GitHub.
2. Cierra completamente cualquier instancia existente de Wallpaper Control desde el área de notificación antes de instalar o actualizar.
3. Ejecuta el instalador.
4. Selecciona opcionalmente un acceso directo en el escritorio durante la instalación.
5. Inicia Wallpaper Control desde el menú Inicio o el acceso directo del escritorio.

Wallpaper Control se instala para el usuario actual de Windows y **no requiere privilegios de administrador**.

El instalador incluye el **runtime de .NET** necesario, por lo que no hace falta instalar .NET por separado. Se crea automáticamente un acceso directo en el menú Inicio y el del escritorio es opcional.

Al actualizar una instalación existente se conservan la configuración y las estadísticas. Si el inicio automático ya estaba activado, su entrada se actualiza para usar la ruta instalada.

La desinstalación elimina la aplicación y sus accesos directos, pero conserva la configuración y las estadísticas para poder reutilizarlas en una instalación posterior.

El instalador actualmente **no está firmado digitalmente**. Por ello, Windows puede mostrar una advertencia de seguridad al ejecutarlo.

## 🔒 Privacidad

Wallpaper Control almacena localmente en tu equipo la configuración y las estadísticas de fondos.

No se requiere una cuenta de Wallpaper Control.

La mayoría de las funciones, incluida la gestión de fondos, la presentación, las transiciones, la detección de pantalla completa y las estadísticas, funcionan completamente de forma local.

Algunas funciones opcionales requieren conexión a Internet:

- El **widget Tiempo** se conecta a Open-Meteo para obtener información meteorológica.
- El **widget Calendario** se conecta a las direcciones iCalendar / ICS configuradas para obtener datos.
- La **comprobación de actualizaciones** opcional se conecta a GitHub Releases para determinar si existe una versión más reciente. Las comprobaciones automáticas pueden desactivarse y Wallpaper Control nunca descarga ni instala actualizaciones automáticamente.

Las direcciones ICS privadas configuradas para el widget Calendario se almacenan cifradas mediante Windows Data Protection API (DPAPI) para el usuario actual de Windows.

El acceso al calendario es de solo lectura. Wallpaper Control no modifica citas ni datos del calendario.

Los registros de diagnóstico se almacenan localmente y solo se crean cuando son necesarios para resolver problemas.

## 🛠️ Desarrollado con

- C#
- .NET 10
- Windows Forms
- API nativas de Windows / integración COM
- Open-Meteo
- iCalendar / ICS

## 📄 Licencia

Copyright (c) 2026 Yasmin Mahr

Wallpaper Control es software libre y de código abierto bajo la **GNU General Public License v3.0 (GPL-3.0)**.

Puedes usar, estudiar, modificar y redistribuir Wallpaper Control libremente conforme a los términos de la GNU General Public License v3.0.

Consulta el archivo `LICENSE` para ver el texto completo de la licencia.

---

**Wallpaper Control**  
Un poco más de control sobre lo que Windows pone en tu escritorio. 🖼️
