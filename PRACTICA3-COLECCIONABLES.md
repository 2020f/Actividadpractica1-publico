# Práctica 3: búsqueda de objetos

Rama: `esta_si_es_la_practica_3`. Proyecto: Unity **6000.6.4f1**.
Escena: `Assets/Scenes/nivel1.unity`.

## Jugar

Abre `nivel1` y pulsa **Play**. Usa **A/D** o las **flechas** para caminar y
**Espacio** para saltar. Busca los cinco alimentos distribuidos entre el suelo y
las plataformas. Al tocarlos desaparecen y **ContadorJuego** avanza desde
`Objetos: 0 / 5` hasta `Objetos: 5 / 5`. Al iniciar otra partida vuelve a cero.

## Requisitos implementados

| Requisito | Configuración guardada |
| --- | --- |
| Recursos dentro de Assets | Cinco PNG transparentes en `Assets/Sprites/Coleccionables`, importados como sprites individuales a 64 píxeles por unidad. |
| Objetos distintos | `Collectibles` contiene **Cake**, **Coffee**, **Cookie**, **Burger** y **Candy**, cada uno con su propio SpriteRenderer. |
| Detección | Cada alimento tiene un **BoxCollider2D** de 0.7 × 0.7 con **Is Trigger** activado. |
| Identificación | Tag **Collectible** y layer **Collectibles**, registrados en TagManager y asignados a los cinco objetos. |
| Interfaz | `Canvas` usa **Screen Space - Camera**, referencia a **Main Camera** y escalado sobre 1280 × 720. |
| Texto | `Canvas/PanelContador/ContadorJuego` usa **TextMeshProUGUI**, fuente Liberation Sans SDF, tamaño 32 y negrita. Incluye los recursos esenciales de TMP y su licencia. |
| Referencia al controlador | El campo serializado **Contador Juego** de `MovimientoJugador` apunta al TMP de la escena; **Total Coleccionables** vale 5. |
| Recolección | `OnTriggerEnter2D` comprueba tag y capa, desactiva el objeto para impedir incrementos repetidos, suma uno, actualiza el texto y destruye el alimento en Play. |

Se conserva el movimiento y el Animator del zombi, junto con el material sin
fricción y el salto de 8.5 que permiten superar las esquinas y llegar al primer
piso. La documentación de esa base está en [PRACTICA3.md](PRACTICA3.md).

## Validación

Con Play detenido, **Práctica 3 → Validar coleccionables** revisa la escena,
los recursos y el contador sobre copias temporales en Edit Mode.

Para comprobar los callbacks físicos y la destrucción real se incluye
`Assets/Editor/ValidarColeccionablesEnJuego.cs`. Se ejecuta en una instancia
independiente de Unity con `-batchmode -executeMethod
ValidarColeccionablesEnJuego.Run`, sin `-quit`; entra en Play, desplaza el cuerpo
del jugador hasta cada trigger, espera los pasos físicos y termina al verificar
el contador en 5 / 5. Si hay GPU, guarda capturas inicial y final en `Logs`.

Verificado con Unity **6000.6.4f1**:

- **83 comprobaciones, 0 errores** de configuración y lógica de coleccionables.
- **144 comprobaciones, 0 errores** de movimiento, animaciones y física,
  incluyendo esquinas, caída y acceso a la primera plataforma.
- **28 comprobaciones, 0 errores** en Play Mode con callbacks físicos reales,
  destrucción de los cinco alimentos y render de la cámara a PNG. Se revisaron
  visualmente `Logs/coleccionables-inicio.png` y
  `Logs/coleccionables-completo.png`, con el contador en 0 / 5 y 5 / 5.

## Recursos y licencia

Los cinco alimentos son de [Kenney Food Kit 2.0](https://kenney.nl/assets/food-kit),
bajo **CC0**. Se conservan la licencia original y la correspondencia de archivos
en `Assets/Sprites/Coleccionables/Kenney-License.txt` y
`Assets/Sprites/Coleccionables/FUENTE-Y-LICENCIA.txt`.
