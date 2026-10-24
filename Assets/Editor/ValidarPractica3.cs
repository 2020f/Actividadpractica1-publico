using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Ejecutar: Unity -batchmode -projectPath ... -executeMethod ValidarPractica3.Run -logFile ...
// Usa los assets reales y el solver 2D de Unity, sin guardar ni modificar las escenas.
public static class ValidarPractica3
{
    private const string ScenePath = "Assets/Scenes/nivel1.unity";
    private const string AnimationPath = "Assets/Animaciones/";
    private const float Dt = 0.02f;
    private static int checks;
    private static readonly List<string> errors = new List<string>();

    [MenuItem("Práctica 3/Validar")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Deten Play antes de validar la practica 3.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        checks = 0;
        errors.Clear();
        try
        {
            ValidateAssets();
            ValidateSceneAndPhysics();
        }
        catch (Exception ex)
        {
            errors.Add("Excepcion durante la validacion: " + ex);
        }

        foreach (string error in errors)
            Debug.LogError("PRACTICA3 FAIL: " + error);
        string summary = $"PRACTICA3 {(errors.Count == 0 ? "PASS" : "FAIL")}: {checks} comprobaciones, {errors.Count} errores.";
        if (errors.Count == 0) Debug.Log(summary);
        else Debug.LogError(summary);
        if (Application.isBatchMode) EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
    }

    private static void Check(bool passed, string description)
    {
        checks++;
        if (!passed) errors.Add(description);
    }

    private static Sprite Sprite(string pose)
    {
        return AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/zombie_" + pose + ".png")
            .OfType<Sprite>().SingleOrDefault();
    }

    private static void ValidateAssets()
    {
        foreach (string pose in new[] { "idle", "stand", "walk1", "walk2", "jump", "fall" })
        {
            string path = "Assets/Sprites/zombie_" + pose + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Check(importer != null && importer.textureType == TextureImporterType.Sprite &&
                  importer.spriteImportMode == SpriteImportMode.Multiple,
                  path + ": importar como Sprite (Multiple).");
            Sprite sprite = Sprite(pose);
            Check(sprite != null, path + ": debe contener exactamente un sprite subdividido.");
            if (sprite == null) continue;
            Check(sprite.name == "zombie_" + pose + "_0", path + ": conservar el nombre del sub-sprite.");
            Check(sprite.rect == new Rect(0, 0, 80, 110), path + ": rectangulo completo 80 x 110.");
            Check(Mathf.Approximately(sprite.pixelsPerUnit, 100), path + ": PPU 100.");
            Check(Vector2.Distance(sprite.pivot, new Vector2(40, 45)) < 0.001f,
                  path + ": pivote comun (40,45) pixeles.");
        }

        AnimationClip idle = Clip("Quieto.anim", "Idle", true);
        AnimationClip walk = Clip("Caminar.anim", "Caminar", true);
        AnimationClip jump = Clip("Salto.anim", "Salto", false);
        if (idle != null)
        {
            var keys = SpriteKeys(idle);
            Check(keys.Any(k => k.value == Sprite("idle")) && keys.Any(k => k.value == Sprite("stand")),
                  "Idle debe alternar las poses idle y stand.");
        }
        if (walk != null)
        {
            Check(Mathf.Approximately(walk.frameRate, 8), "Caminar debe usar 8 muestras/segundo.");
            var keys = SpriteKeys(walk);
            Check(keys.Any(k => k.value == Sprite("walk1")) && keys.Any(k => k.value == Sprite("walk2")),
                  "Caminar debe referenciar las dos poses de caminar.");
        }
        if (jump != null)
        {
            Check(Mathf.Approximately(jump.frameRate, 5), "Salto debe usar 5 muestras/segundo.");
            var keys = SpriteKeys(jump);
            Check(keys.Any(k => Mathf.Approximately(k.time, 0) && k.value == Sprite("jump")),
                  "Salto debe comenzar con zombie_jump.");
            Check(keys.Any(k => Mathf.Abs(k.time - 0.4f) < 0.001f && k.value == Sprite("fall")),
                  "Salto debe cambiar a zombie_fall en 0.4 segundos.");
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimationPath + "Jugador.controller");
        Check(controller != null, "Jugador.controller debe importar como AnimatorController.");
        if (controller == null || controller.layers.Length == 0) return;
        Check(controller.layers.Length == 1 && controller.layers[0].name == "Base Layer",
              "El Animator debe tener una sola Base Layer.");
        Check(controller.parameters.Length == 2 && new[] { "Caminando", "Saltando" }.All(name =>
                  controller.parameters.Any(p => p.name == name && p.type == AnimatorControllerParameterType.Bool)),
              "El Animator debe declarar los bool Caminando y Saltando.");
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var states = machine.states.Select(s => s.state).ToArray();
        Check(states.Length == 3 && new[] { "Idle", "Caminar", "Salto" }.All(n => states.Any(s => s.name == n)),
              "La capa debe contener Idle, Caminar y Salto.");
        Check(machine.defaultState != null && machine.defaultState.name == "Idle", "Idle debe ser el estado inicial.");
        Check(machine.anyStateTransitions.Length == 0 && machine.entryTransitions.Length == 0 &&
              machine.stateMachines.Length == 0, "El grafo debe usar las seis transiciones directas entre los tres estados.");
        foreach (AnimatorState state in states)
        {
            AnimationClip expected = state.name == "Idle" ? idle : state.name == "Caminar" ? walk : jump;
            Check(state.motion == expected && expected != null, state.name + ": clip nativo correcto.");
            Check(state.transitions.Length == 2, state.name + ": dos transiciones salientes.");
            foreach (AnimatorStateTransition transition in state.transitions)
            {
                string destination = transition.destinationState != null ? transition.destinationState.name : "(ninguno)";
                string arc = state.name + " -> " + destination;
                Check(destination != state.name && states.Contains(transition.destinationState), arc + ": destino valido.");
                Check(!transition.hasExitTime && Mathf.Approximately(transition.duration, 0),
                      arc + ": sin Exit Time y con duracion cero.");
                var expectedConditions = new Dictionary<string, AnimatorConditionMode>();
                expectedConditions["Saltando"] = destination == "Salto" ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                if (destination != "Salto")
                    expectedConditions["Caminando"] = destination == "Caminar" ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                Check(transition.conditions.Length == expectedConditions.Count && transition.conditions.All(c =>
                    expectedConditions.TryGetValue(c.parameter, out AnimatorConditionMode mode) && mode == c.mode),
                    arc + ": condiciones de suelo, salto y movimiento correctas.");
            }
            Check(states.Where(s => s != state).All(dst => state.transitions.Count(t => t.destinationState == dst) == 1),
                  state.name + ": exactamente una transicion hacia cada otro estado.");
        }
    }

    private static AnimationClip Clip(string file, string name, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationPath + file);
        Check(clip != null, file + ": clip importado.");
        if (clip == null) return null;
        Check(clip.name == name && clip.isLooping == loop, file + ": nombre y repeticion correctos.");
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        Check(bindings.Length == 1 && bindings[0].type == typeof(SpriteRenderer) &&
              bindings[0].propertyName == "m_Sprite" && bindings[0].path == "", file + ": animar el SpriteRenderer del jugador.");
        Check(SpriteKeys(clip).Length > 0 && SpriteKeys(clip).All(k => k.value is Sprite), file + ": cuadros sin referencias rotas.");
        return clip;
    }

    private static ObjectReferenceKeyframe[] SpriteKeys(AnimationClip clip)
    {
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        return AnimationUtility.GetObjectReferenceCurve(clip, binding) ?? Array.Empty<ObjectReferenceKeyframe>();
    }

    private static void ValidateSceneAndPhysics()
    {
        // Batch arranca con una escena vacia sin guardar; no contiene trabajo del editor.
        if (Application.isBatchMode)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                throw new InvalidOperationException("Guarda o cierra las escenas sin titulo antes de validar.");

        Scene previousActive = SceneManager.GetActiveScene();
        Scene sourceScene = SceneManager.GetSceneByPath(ScenePath);
        bool openedSource = !sourceScene.isLoaded;
        Scene testScene = default;
        var previousSimulation = Physics2D.simulationMode;
        InputSettings previousInputSettings = InputSystem.settings;
        InputSettings inputSettingsBackup = null;
        InputSettings testInputSettings = null;
        Keyboard previousKeyboard = Keyboard.current;
        Keyboard keyboard = null;
        try
        {
            if (openedSource) sourceScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var players = sourceScene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MovimientoJugador>(true)).ToArray();
            Check(players.Length == 1, "nivel1 debe tener exactamente un jugador con MovimientoJugador.");
            if (players.Length != 1) return;
            MovimientoJugador source = players[0];
            var box = source.GetComponent<BoxCollider2D>();
            Check(box != null && !box.isTrigger && box.enabled && Vector2.Distance(box.size, new Vector2(0.62f, 0.9f)) < 0.001f &&
                  box.offset == Vector2.zero, "Jugador: BoxCollider2D activo, solido, 0.62 x 0.9, offset cero.");
            Check(source.GetComponents<Collider2D>().Length == 1 && source.GetComponent<CapsuleCollider2D>() == null,
                  "Jugador: un unico collider BoxCollider2D.");
            var requiredTypes = typeof(MovimientoJugador).GetCustomAttributes(typeof(RequireComponent), true)
                .Cast<RequireComponent>().SelectMany(r => new[] { r.m_Type0, r.m_Type1, r.m_Type2 }).ToArray();
            Check(requiredTypes.Contains(typeof(BoxCollider2D)) && !requiredTypes.Contains(typeof(CapsuleCollider2D)),
                  "MovimientoJugador requiere BoxCollider2D y no vuelve a agregar una capsula.");
            Check(source.GetComponent<SpriteRenderer>() != null && source.GetComponent<SpriteRenderer>().sprite == Sprite("idle"),
                  "Jugador: sprite inicial zombie_idle.");
            Check(source.GetComponent<Animator>() != null && source.GetComponent<Animator>().runtimeAnimatorController ==
                  AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AnimationPath + "Jugador.controller"),
                  "Jugador: controller asignado al Animator.");
            var enabledScenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
            Check(enabledScenes.Length > 0 && enabledScenes[0].path == ScenePath, "nivel1 debe ser la primera escena habilitada del build.");
            if (box == null || source.GetComponent<Rigidbody2D>() == null || source.GetComponent<Animator>() == null) return;

            // En modo Editor no se puede crear una escena con LocalPhysicsMode.
            // Simulate(dt, mask) solo avanza cuerpos y contactos de las capas indicadas.
            // Elegir una capa libre evita modificar los cuerpos de las escenas abiertas.
            var occupiedLayers = new HashSet<int>(Resources.FindObjectsOfTypeAll<Collider2D>()
                .Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded).Select(c => c.gameObject.layer));
            occupiedLayers.UnionWith(Resources.FindObjectsOfTypeAll<Rigidbody2D>()
                .Where(b => b.gameObject.scene.IsValid() && b.gameObject.scene.isLoaded).Select(b => b.gameObject.layer));
            int testLayer = Enumerable.Range(8, 24).Reverse().FirstOrDefault(layer =>
                !occupiedLayers.Contains(layer) && !Physics2D.GetIgnoreLayerCollision(layer, layer));
            if (testLayer < 8) throw new InvalidOperationException("No hay una capa de fisica libre para aislar la prueba.");
            testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(testScene);
            var clone = Object.Instantiate(source.gameObject);
            clone.name = "Jugador_Validacion";
            SceneManager.MoveGameObjectToScene(clone, testScene);
            foreach (Transform part in clone.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = testLayer;
            var floor = new GameObject("Suelo_Validacion");
            SceneManager.MoveGameObjectToScene(floor, testScene);
            floor.layer = testLayer;
            floor.transform.position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(20, 1);
            var wall = new GameObject("Pared_Validacion");
            SceneManager.MoveGameObjectToScene(wall, testScene);
            wall.layer = testLayer;
            wall.transform.position = new Vector3(2, 4, 0);
            var wallCollider = wall.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(1, 8);
            wall.SetActive(false);

            if (Physics2D.simulationMode != SimulationMode2D.Script) Physics2D.simulationMode = SimulationMode2D.Script;
            // El buffer Editor no actualiza el contador por dispositivo usado por
            // wasPressedThisFrame. Ejecutar el ciclo Manual real, como en las pruebas
            // del paquete Input System; la flag solo existe en esta copia temporal.
            // InputManager destruye los settings transitorios HideAndDontSave al
            // reemplazarlos. Conservar una copia para restaurar tambien ese caso.
            if (!EditorUtility.IsPersistent(previousInputSettings))
            {
                inputSettingsBackup = Object.Instantiate(previousInputSettings);
                inputSettingsBackup.name = previousInputSettings.name;
                inputSettingsBackup.hideFlags = HideFlags.HideAndDontSave;
            }
            testInputSettings = Object.Instantiate(previousInputSettings);
            testInputSettings.hideFlags = HideFlags.HideAndDontSave;
            testInputSettings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            testInputSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            InputSystem.settings = testInputSettings;
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            var simulation = new Simulation(clone, 1 << testLayer, keyboard);
            simulation.Reset(new Vector2(0, 0.46f));
            var filterField = typeof(MovimientoJugador).GetField("filtroSuelo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (filterField != null && filterField.GetValue(clone.GetComponent<MovimientoJugador>()) is ContactFilter2D filter)
                Check(filter.useNormalAngle && !filter.useTriggers && Mathf.Approximately(filter.minNormalAngle, 45) &&
                      Mathf.Approximately(filter.maxNormalAngle, 135), "El filtro de suelo admite soporte inferior entre 45 y 135 grados.");
            else Check(false, "MovimientoJugador configura su ContactFilter2D en Awake.");
            simulation.Steps(15);
            simulation.Expect("Idle", false, false, "reposo sobre suelo");
            Check(Mathf.Abs(simulation.Body.position.y - 0.45f) < 0.03f, "El collider apoya los pies a la altura del suelo.");
            simulation.Steps(3, Key.D);
            simulation.Expect("Caminar", true, false, "caminar a la derecha con D");
            Debug.Log($"PRACTICA3 TRACE caminar: orden={simulation.CommandVelocity}, solver={simulation.Body.linearVelocity}, input={InputState.currentUpdateType}, contador={InputState.updateCount}.");
            Check(Mathf.Approximately(simulation.CommandVelocity.x, 5) && simulation.Body.linearVelocity.x > 0 && !simulation.Image.flipX,
                  "D ordena velocidad 5, mueve a la derecha y orienta el sprite.");
            simulation.Steps(3, Key.LeftArrow);
            simulation.Expect("Caminar", true, false, "caminar a la izquierda con flecha");
            Check(Mathf.Approximately(simulation.CommandVelocity.x, -5) && simulation.Body.linearVelocity.x < 0 && simulation.Image.flipX,
                  "Flecha izquierda ordena velocidad -5, mueve y orienta el sprite.");
            simulation.Steps(2, Key.A);
            Check(Mathf.Approximately(simulation.CommandVelocity.x, -5) && simulation.Body.linearVelocity.x < 0, "A tambien mueve a la izquierda.");
            simulation.Steps(2, Key.RightArrow);
            Check(Mathf.Approximately(simulation.CommandVelocity.x, 5) && simulation.Body.linearVelocity.x > 0, "Flecha derecha tambien mueve a la derecha.");
            simulation.Steps(2, Key.A, Key.D);
            simulation.Expect("Idle", false, false, "direcciones opuestas se cancelan");
            Check(Mathf.Abs(simulation.Body.linearVelocity.x) < 0.001f, "A+D no desplaza al jugador.");

            simulation.Step(Key.Space);
            Debug.Log($"PRACTICA3 TRACE Space: pulsacion={simulation.SpacePressed}, input={InputState.currentUpdateType}, contador={InputState.updateCount}, orden={simulation.CommandVelocity}, solver={simulation.Body.linearVelocity}, Saltando={simulation.Animator.GetBool("Saltando")}.");
            Check(simulation.SpacePressed, "El teclado sintetico entrega una pulsacion real wasPressedThisFrame de Space.");
            simulation.Expect("Salto", false, true, "inicio inmediato del salto");
            Check(simulation.Body.linearVelocity.y > 7, "Space aplica impulso de salto al estar apoyado.");
            simulation.Step(Key.D);
            float ascendingSpeed = simulation.Body.linearVelocity.y;
            simulation.Step(Key.D, Key.Space);
            Check(simulation.Body.linearVelocity.y < ascendingSpeed, "Una nueva pulsacion en el aire no produce doble salto.");
            simulation.Expect("Salto", false, true, "moverse en el aire conserva Salto");
            Check(Mathf.Approximately(simulation.CommandVelocity.x, 5) && simulation.Body.linearVelocity.x > 0, "El control horizontal sigue disponible en el aire.");
            for (int i = 0; i < 60 && simulation.Body.linearVelocity.y >= -0.5f; i++) simulation.Step(Key.D);
            simulation.Expect("Salto", false, true, "descenso del salto");
            Check(simulation.Image.sprite == Sprite("fall"), "El clip muestra zombie_fall durante el descenso.");
            bool landed = false;
            for (int i = 0; i < 90; i++)
            {
                simulation.Step(Key.D);
                if (!simulation.Animator.GetBool("Saltando")) { landed = true; break; }
            }
            Check(landed, "El jugador aterriza sin quedar bloqueado en Salto.");
            simulation.Expect("Caminar", true, false, "aterrizar manteniendo direccion");
            simulation.Steps(2);
            simulation.Expect("Idle", false, false, "detenerse tras aterrizar");

            simulation.Reset(new Vector2(9.5f, 0.46f));
            simulation.Steps(15);
            simulation.Steps(18, Key.D);
            simulation.Expect("Salto", false, true, "caminar fuera de una plataforma");
            Check(simulation.Body.position.y < 0.4f, "Salir del borde inicia caida fisica.");

            wall.SetActive(true);
            simulation.Reset(new Vector2(1.17f, 3));
            simulation.Body.gravityScale = 0;
            simulation.Steps(10, Key.D);
            Check(simulation.Collider.IsTouching(wallCollider), "El ensayo lateral establece contacto real con una pared.");
            float wallHeight = simulation.Body.position.y;
            simulation.Step(Key.D, Key.Space);
            simulation.Expect("Salto", false, true, "pared lateral no cuenta como suelo");
            Check(Mathf.Abs(simulation.Body.linearVelocity.y) < 0.001f && Mathf.Abs(simulation.Body.position.y - wallHeight) < 0.02f,
                  "Estar contra una pared no permite saltar sin apoyo inferior.");

            // Mantener la direccion contra un lateral no debe anular la gravedad.
            // Se prueba a ambos lados, con la gravedad real y sin pulsar salto.
            foreach (int direction in new[] { -1, 1 })
            {
                Key key = direction < 0 ? Key.A : Key.D;
                wall.transform.position = new Vector3(direction * 2, 4, 0);
                simulation.Reset(new Vector2(direction * 1.17f, 3));
                float initialHeight = simulation.Body.position.y;
                simulation.Steps(10, key);
                string side = direction < 0 ? "izquierda" : "derecha";
                Check(simulation.Collider.IsTouching(wallCollider), "Caida contra pared " + side + ": contacto real sostenido.");
                Check(simulation.Body.position.y < initialHeight - 0.2f && simulation.Body.linearVelocity.y < -2,
                      "Presionar la pared " + side + " no debe dejar al jugador suspendido.");
            }
            wall.SetActive(false);

            // Reproducir la esquina del piso fino usando su geometria y sus
            // componentes reales; no sustituirlo por una pared de prueba.
            GameObject sourcePlatform = sourceScene.GetRootGameObjects().SingleOrDefault(g => g.name == "Plataforma1");
            Check(sourcePlatform != null, "Existe Plataforma1 para reproducir las esquinas reales del nivel.");
            if (sourcePlatform != null)
            {
                GameObject ledge = Object.Instantiate(sourcePlatform);
                ledge.name = "Esquina_Validacion";
                SceneManager.MoveGameObjectToScene(ledge, testScene);
                foreach (Transform part in ledge.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = testLayer;
                ledge.transform.position = new Vector3(0, 3, 0);
                Physics2D.SyncTransforms();
                var ledgeCollider = ledge.GetComponent<BoxCollider2D>();
                Check(ledgeCollider != null, "La plataforma clonada conserva su BoxCollider2D.");
                if (ledgeCollider != null)
                {
                    foreach (int side in new[] { -1, 1 })
                    {
                        Key key = side < 0 ? Key.D : Key.A;
                        Bounds bounds = ledgeCollider.bounds;
                        float edge = side < 0 ? bounds.min.x : bounds.max.x;
                        // Los pies comienzan 0.20 unidades bajo la superficie:
                        // debe rozar el lateral y caer, sin engancharse al borde.
                        simulation.Reset(new Vector2(edge + side * (simulation.Collider.size.x * 0.5f + 0.02f), bounds.max.y + 0.25f));
                        float initialHeight = simulation.Body.position.y;
                        simulation.Steps(10, key);
                        string corner = side < 0 ? "izquierda" : "derecha";
                        Check(simulation.Collider.IsTouching(ledgeCollider), "Esquina " + corner + ": contacto real con la plataforma fina.");
                        Check(simulation.Body.position.y < initialHeight - 0.2f && simulation.Body.linearVelocity.y < -2,
                              "La esquina " + corner + " del piso no debe dejar al jugador pegado al presionar hacia ella.");
                    }

                    // El primer salto tambien debe alcanzar el piso real: quitar
                    // friccion evita colgarse, pero no compensa un salto corto.
                    GameObject sourceGround = sourceScene.GetRootGameObjects().SingleOrDefault(g => g.name == "Suelo");
                    Check(sourceGround != null, "Existe Suelo para validar el primer salto real del nivel.");
                    if (sourceGround != null)
                    {
                        floor.SetActive(false);
                        ledge.transform.position = sourcePlatform.transform.position;
                        GameObject ground = Object.Instantiate(sourceGround);
                        ground.name = "Suelo_Real_Validacion";
                        SceneManager.MoveGameObjectToScene(ground, testScene);
                        foreach (Transform part in ground.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = testLayer;
                        simulation.Reset(new Vector2(-6.2f, -3.27f));
                        simulation.Steps(15);
                        simulation.Step(Key.D, Key.Space);
                        bool landedOnRealPlatform = false;
                        float highestFeet = simulation.Collider.bounds.min.y;
                        for (int i = 0; i < 45; i++)
                        {
                            simulation.Step(Key.D);
                            highestFeet = Mathf.Max(highestFeet, simulation.Collider.bounds.min.y);
                            Bounds platformBounds = ledgeCollider.bounds;
                            if (simulation.Collider.IsTouching(ledgeCollider) &&
                                simulation.Body.position.x > platformBounds.min.x &&
                                simulation.Collider.bounds.min.y >= platformBounds.max.y - 0.03f &&
                                simulation.Body.linearVelocity.y <= 0.1f)
                            {
                                landedOnRealPlatform = true;
                                break;
                            }
                        }
                        Debug.Log($"PRACTICA3 TRACE primer piso: altura maxima pies={highestFeet:F4}, superficie={ledgeCollider.bounds.max.y:F4}, aterriza={landedOnRealPlatform}.");
                        Check(landedOnRealPlatform, "D+Space debe alcanzar y aterrizar sobre Plataforma1 desde el Suelo real, sin quedar contra su esquina.");
                    }
                }
            }
            Debug.Log("PRACTICA3: assets, escena, teclado y fisica nativa: recorrido completado.");
        }
        finally
        {
            try
            {
                try
                {
                    if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                    if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                }
                finally
                {
                    try
                    {
                        InputSettings restore = previousInputSettings != null ? previousInputSettings : inputSettingsBackup;
                        if (restore == null) throw new InvalidOperationException("No se pudieron recuperar los InputSettings originales.");
                        if (InputSystem.settings != restore) InputSystem.settings = restore;
                    }
                    finally
                    {
                        // El manager pasa a ser propietario de la copia restaurada.
                        if (testInputSettings != null && InputSystem.settings != testInputSettings) Object.DestroyImmediate(testInputSettings);
                        if (inputSettingsBackup != null && InputSystem.settings != inputSettingsBackup) Object.DestroyImmediate(inputSettingsBackup);
                    }
                }
            }
            finally
            {
                // Una excepcion de Input System no puede dejar Script ni escenas temporales activos.
                try
                {
                    if (Physics2D.simulationMode != previousSimulation) Physics2D.simulationMode = previousSimulation;
                }
                finally
                {
                    try
                    {
                        if (testScene.IsValid() && testScene.isLoaded) EditorSceneManager.CloseScene(testScene, true);
                    }
                    finally
                    {
                        try
                        {
                            if (openedSource && sourceScene.IsValid() && sourceScene.isLoaded) EditorSceneManager.CloseScene(sourceScene, true);
                        }
                        finally
                        {
                            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                        }
                    }
                }
            }
        }
    }

    private sealed class Simulation
    {
        public readonly Rigidbody2D Body;
        public readonly Animator Animator;
        public readonly SpriteRenderer Image;
        public readonly BoxCollider2D Collider;
        public Vector2 CommandVelocity { get; private set; }
        public bool SpacePressed { get; private set; }
        private readonly MovimientoJugador movement;
        private readonly int simulationMask;
        private readonly Keyboard keyboard;
        private readonly MethodInfo awake;
        private readonly MethodInfo update;
        private readonly MethodInfo fixedUpdate;
        private readonly float gravity;

        public Simulation(GameObject player, int simulationMask, Keyboard keyboard)
        {
            Body = player.GetComponent<Rigidbody2D>();
            Animator = player.GetComponent<Animator>();
            Image = player.GetComponent<SpriteRenderer>();
            Collider = player.GetComponent<BoxCollider2D>();
            movement = player.GetComponent<MovimientoJugador>();
            this.simulationMask = simulationMask;
            this.keyboard = keyboard;
            gravity = Body.gravityScale;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            awake = typeof(MovimientoJugador).GetMethod("Awake", flags);
            update = typeof(MovimientoJugador).GetMethod("Update", flags);
            fixedUpdate = typeof(MovimientoJugador).GetMethod("FixedUpdate", flags);
            if (awake == null || update == null || fixedUpdate == null)
                throw new InvalidOperationException("MovimientoJugador requiere Awake, Update y FixedUpdate privados.");
        }

        public void Reset(Vector2 position)
        {
            Body.position = position;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            Body.gravityScale = gravity;
            Body.WakeUp();
            awake.Invoke(movement, null);
            Animator.Rebind();
            Animator.Update(0);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Physics2D.SyncTransforms();
        }

        public void Steps(int count, params Key[] keys)
        {
            for (int i = 0; i < count; i++) Step(keys);
        }

        public void Step(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
            SpacePressed = keyboard.spaceKey.wasPressedThisFrame;
            update.Invoke(movement, null);
            fixedUpdate.Invoke(movement, null);
            // La friccion del solver puede reducir despues la velocidad horizontal.
            CommandVelocity = Body.linearVelocity;
            Animator.Update(Dt);
            if (!Physics2D.Simulate(Dt, simulationMask))
                throw new InvalidOperationException("Unity rechazo el paso de simulacion 2D.");
        }

        public void Expect(string state, bool walking, bool jumping, string situation)
        {
            Check(Animator.GetBool("Caminando") == walking && Animator.GetBool("Saltando") == jumping,
                  situation + ": parametros Caminando/Saltando.");
            Check(Animator.GetCurrentAnimatorStateInfo(0).shortNameHash == UnityEngine.Animator.StringToHash(state),
                  situation + ": estado visible " + state + ".");
        }
    }
}
