using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Ejecutar en una instancia independiente con Unity -batchmode
// -projectPath ... -executeMethod ValidarColeccionablesEnJuego.Run -logFile ...
// Entramos en Play Mode y dejamos avanzar el ciclo real de Unity. Este archivo
// nunca llama OnTriggerEnter2D ni sus metodos auxiliares: el solver 2D debe
// generar los callbacks, y Destroy debe completarse al final del frame.
public static class ValidarColeccionablesEnJuego
{
    private const string Prefix = "ValidarColeccionablesEnJuego.";
    private const string PendingKey = Prefix + "pending";
    private const string StageKey = Prefix + "stage";
    private const string ResultKey = Prefix + "result";
    private const string ScenePath = "Assets/Scenes/nivel1.unity";
    private const double OverallTimeout = 20;
    private const double ItemTimeout = 1;
    private static readonly string[] Names = { "Cake", "Coffee", "Cookie", "Burger", "Candy" };

    [Serializable]
    private sealed class Result
    {
        public int checks;
        public List<string> errors = new List<string>();
    }

    private enum Phase { InitialWait, InitialCaptureWait, Collecting, FinalCaptureWait }

    private static Result result;
    private static bool initialized;
    private static bool reporting;
    private static Rigidbody2D body;
    private static TextMeshProUGUI counter;
    private static GameObject[] items;
    private static Phase phase;
    private static int itemIndex;
    private static double startedAt;
    private static double phaseStartedAt;
    private static double targetFixedTime;
    private static int targetFrame;
    private static string logFolder;
    private static bool canCapture;

    [InitializeOnLoadMethod]
    private static void Register()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update -= Tick;
        if (!SessionState.GetBool(PendingKey, false)) return;
        EditorApplication.update += Tick;
        if (SessionState.GetInt(StageKey, 0) == 2 && !EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.delayCall += Report;
    }

    public static void Run()
    {
        // El ejecutable batch es una instancia independiente: no sustituimos
        // escenas ni entramos en Play en un editor interactivo del usuario.
        if (!Application.isBatchMode)
        {
            Debug.LogError("Esta prueba de Play Mode requiere una instancia independiente de Unity con -batchmode.");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Deten Play antes de iniciar la prueba de coleccionables.");
            EditorApplication.Exit(1);
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        result = new Result();
        SessionState.SetString(ResultKey, JsonUtility.ToJson(result));
        SessionState.SetInt(StageKey, 0);
        SessionState.SetBool(PendingKey, true);
        SessionState.SetFloat(Prefix + "transitionStartedAt", (float)EditorApplication.timeSinceStartup);
        initialized = false;
        reporting = false;
        Register();
        Debug.Log("COLECCIONABLES PLAY: entrando en Play Mode para probar callbacks fisicos reales.");
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        try
        {
            if (state == PlayModeStateChange.EnteredPlayMode) BeginPlay();
            else if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetInt(StageKey, 0) != 2)
            {
                EnsureResult();
                result.errors.Add("Play Mode termino antes de completar la prueba.");
                SaveResult();
                SessionState.SetInt(StageKey, 2);
            }
            else if (state == PlayModeStateChange.EnteredEditMode) Report();
        }
        catch (Exception exception)
        {
            Fail("Excepcion durante la transicion de Play Mode: " + exception);
        }
    }

    private static void BeginPlay()
    {
        if (initialized) return;
        initialized = true;
        EnsureResult();
        SessionState.SetInt(StageKey, 1);
        startedAt = EditorApplication.timeSinceStartup;
        phaseStartedAt = startedAt;
        phase = Phase.InitialWait;
        itemIndex = 0;
        MovimientoJugador[] players = Object.FindObjectsByType<MovimientoJugador>(FindObjectsSortMode.None);
        TextMeshProUGUI[] counters = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None)
            .Where(text => text.name == "ContadorJuego").ToArray();
        Check(players.Length == 1, "Play: existe un unico MovimientoJugador activo.");
        Check(counters.Length == 1, "Play: existe un unico ContadorJuego TMP activo.");
        if (players.Length != 1 || counters.Length != 1) { StopPlay(); return; }
        body = players[0].GetComponent<Rigidbody2D>();
        counter = counters[0];
        items = Names.Select(GameObject.Find).ToArray();
        Check(body != null && body.simulated && body.bodyType == RigidbodyType2D.Dynamic,
            "Play: el cuerpo del jugador es dinamico y simulado.");
        Check(items.All(item => item != null && item.activeInHierarchy), "Play: los cinco alimentos originales estan activos.");
        Check(counter.text == "Objetos: 0 / 5", "Play: Awake presenta Objetos: 0 / 5.");
        Check(Physics2D.simulationMode == SimulationMode2D.FixedUpdate && Time.timeScale > 0 && !EditorApplication.isPaused,
            "Play: la fisica 2D automatica avanza en FixedUpdate, sin pausa.");
        if (body == null || items.Any(item => item == null) || result.errors.Count > 0) { StopPlay(); return; }

        // Aislar el recorrido del impulso de gravedad; los contactos y callbacks
        // siguen siendo responsabilidad del ciclo nativo de Unity.
        body.gravityScale = 0;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0;
        logFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        canCapture = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        if (canCapture) Directory.CreateDirectory(logFolder);
        else Debug.Log("COLECCIONABLES PLAY: GPU Null; se omiten las capturas opcionales.");
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        try
        {
            int stage = SessionState.GetInt(StageKey, 0);
            if (stage == 2) return;
            if (!initialized)
            {
                if (EditorApplication.isPlaying && stage == 1)
                    BeginPlay();
                else if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Prefix + "transitionStartedAt", 0) > OverallTimeout)
                    Fail("Timeout al entrar en Play Mode.");
                return;
            }
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - startedAt > OverallTimeout) { Fail("Timeout global de 20 segundos en Play Mode."); return; }
            if (phase == Phase.InitialWait)
            {
                if (now - phaseStartedAt < 0.3) return;
                Check(counter.text == "Objetos: 0 / 5" && items.All(item => item != null),
                    "Play: el estado inicial permanece en 0 / 5 antes del recorrido.");
                Capture("coleccionables-inicio.png");
                phase = Phase.InitialCaptureWait;
                phaseStartedAt = now;
            }
            else if (phase == Phase.InitialCaptureWait)
            {
                // Dar margen entre el render inicial y el recorrido fisico.
                if (now - phaseStartedAt < 0.3) return;
                MoveToNextItem();
            }
            else if (phase == Phase.Collecting)
            {
                if (now - phaseStartedAt > ItemTimeout)
                {
                    Fail(Names[itemIndex] + ": no se completo la recoleccion nativa en un segundo. UI actual: " + counter.text);
                    return;
                }
                // No se acepta solo un cambio de texto: debe avanzar un paso
                // FixedUpdate real y otro frame, y Destroy debe haber terminado.
                if (now - phaseStartedAt < 0.15 || Time.fixedTimeAsDouble <= targetFixedTime ||
                    Time.frameCount <= targetFrame || items[itemIndex] != null) return;
                int expected = itemIndex + 1;
                Check(counter.text == $"Objetos: {expected} / 5",
                    Names[itemIndex] + ": el trigger fisico suma una vez y actualiza el TMP.");
                Check(items.Take(expected).All(item => item == null) && items.Skip(expected).All(item => item != null),
                    Names[itemIndex] + ": Destroy elimina solo los alimentos ya recogidos.");
                if (result.errors.Count > 0) { StopPlay(); return; }
                itemIndex++;
                if (itemIndex < Names.Length) MoveToNextItem();
                else
                {
                    Check(counter.text == "Objetos: 5 / 5" && items.All(item => item == null),
                        "Play: los cinco alimentos fueron destruidos y el contador termino en 5 / 5.");
                    Capture("coleccionables-completo.png");
                    phase = Phase.FinalCaptureWait;
                    phaseStartedAt = now;
                }
            }
            else if (phase == Phase.FinalCaptureWait && now - phaseStartedAt >= 0.3)
            {
                Check(counter.text == "Objetos: 5 / 5", "Play: el contador sigue en 5 / 5 sin dobles incrementos.");
                StopPlay();
            }
        }
        catch (Exception exception)
        {
            Fail("Excepcion en la prueba nativa de Play Mode: " + exception);
        }
    }

    private static void MoveToNextItem()
    {
        GameObject item = items[itemIndex];
        BoxCollider2D trigger = item != null ? item.GetComponent<BoxCollider2D>() : null;
        Check(trigger != null && trigger.enabled && trigger.isTrigger,
            Names[itemIndex] + ": conserva un BoxCollider2D trigger activo antes del contacto real.");
        if (trigger == null || !trigger.enabled || !trigger.isTrigger) { StopPlay(); return; }
        body.linearVelocity = Vector2.zero;
        body.position = trigger.bounds.center;
        body.WakeUp();
        Physics2D.SyncTransforms();
        targetFixedTime = Time.fixedTimeAsDouble;
        targetFrame = Time.frameCount;
        phaseStartedAt = EditorApplication.timeSinceStartup;
        phase = Phase.Collecting;
        Debug.Log($"COLECCIONABLES PLAY TRACE: contacto nativo con {Names[itemIndex]}, paso fijo inicial={targetFixedTime:F3}.");
    }

    private static void Capture(string filename)
    {
        if (!canCapture) return;
        string path = Path.Combine(logFolder, filename);
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("No existe Main Camera para la captura nativa.");
        const int width = 1280;
        const int height = 720;
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        float previousAspect = camera.aspect;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "CapturaColeccionables_Validacion",
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false
        };
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        try
        {
            target.Create();
            // Preparar tambien el Canvas Screen Space - Camera para el tamano
            // del destino; asi se captura el HUD real con la escena real.
            camera.targetTexture = target;
            camera.aspect = (float)width / height;
            Canvas.ForceUpdateCanvases();
            counter.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            // URP 17.6 admite StandardRequest y lo procesa secuencialmente.
            // No depende de GameView ni de WaitForEndOfFrame, omitidos en batch.
            var request = new RenderPipeline.StandardRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            image.Apply(false, false);
            byte[] png = image.EncodeToPNG();
            File.WriteAllBytes(path, png);
            Check(png.Length > 1024 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71 &&
                new FileInfo(path).Length == png.Length, filename + ": PNG nativo 1280 x 720 guardado y no vacio.");
            var colors = new HashSet<uint>();
            Color32[] pixels = image.GetPixels32();
            for (int index = 0; index < pixels.Length && colors.Count < 16; index += 97)
            {
                Color32 color = pixels[index];
                colors.Add(((uint)color.r << 24) | ((uint)color.g << 16) | ((uint)color.b << 8) | color.a);
            }
            Check(colors.Count > 2, filename + ": el render contiene varios colores y no es una textura vacia o uniforme.");
            Debug.Log($"COLECCIONABLES PLAY CAPTURA: {path}; {width} x {height}, {png.Length} bytes, muestra={colors.Count} colores.");
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            RenderTexture.active = previousActive;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(image);
            Canvas.ForceUpdateCanvases();
        }
    }

    private static void Check(bool passed, string description)
    {
        result.checks++;
        if (!passed) result.errors.Add(description);
    }

    private static void EnsureResult()
    {
        if (result == null)
            result = JsonUtility.FromJson<Result>(SessionState.GetString(ResultKey, "")) ?? new Result();
    }

    private static void SaveResult()
    {
        SessionState.SetString(ResultKey, JsonUtility.ToJson(result));
    }

    private static void Fail(string message)
    {
        EnsureResult();
        result.errors.Add(message);
        StopPlay();
    }

    private static void StopPlay()
    {
        SaveResult();
        SessionState.SetInt(StageKey, 2);
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else Report();
    }

    private static void Report()
    {
        if (reporting || !SessionState.GetBool(PendingKey, false)) return;
        reporting = true;
        EnsureResult();
        foreach (string error in result.errors) Debug.LogError("COLECCIONABLES PLAY FAIL: " + error);
        string conclusion = result.errors.Count == 0
            ? "; callbacks fisicos y Destroy reales verificados en Play Mode."
            : "; prueba nativa de Play Mode incompleta o fallida.";
        string summary = $"COLECCIONABLES PLAY {(result.errors.Count == 0 ? "PASS" : "FAIL")}: {result.checks} comprobaciones, {result.errors.Count} errores" + conclusion;
        if (result.errors.Count == 0) Debug.Log(summary);
        else Debug.LogError(summary);
        SessionState.SetBool(PendingKey, false);
        SessionState.EraseInt(StageKey);
        SessionState.EraseString(ResultKey);
        SessionState.EraseFloat(Prefix + "transitionStartedAt");
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(result.errors.Count == 0 ? 0 : 1);
    }
}
