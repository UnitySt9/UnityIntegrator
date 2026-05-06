using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Editor.Tools.AssetIntegrator
{
    public class AnimalIntegrator : IIntegrationStep
    {
        // === Конфигурация ===
        private const string AnimalFbxPath = "Assets/_Project/Art/Animals/FBX/BlackTailedDeer/BlackTailedDeer_skinned.fbx";
        private const string AnimalMaterialPath = "Assets/_Project/Art/Animals/Materials/BlackTailedDeer_Mat.mat";
        private const string AnimalPrefabPath = "Assets/_Project/Prefabs/Animals/BlackTailedDeer_skinned.prefab";
        private const string AnimatorControllerPath = "Assets/_Project/Art/Animals/FBX/BlackTailedDeer/Animations/BlackTailedDeer_Animator.controller";

        // === Состояние ===
        public string StepName => "Интеграция животного и анимаций";
        public StepStatus Status { get; private set; } = StepStatus.Pending;
        public string Log { get; private set; } = "";
        public bool IsEnabled { get; set; } = true;
        public bool AutoFix { private get; set; } = false;

        private void AddLog(string msg, bool isError = false)
        {
            Log += (isError ? "[ERROR] " : "[OK] ") + msg + "\n";
            Debug.Log(msg);
            if (isError) Status = StepStatus.Error;
            else if (Status != StepStatus.Error) Status = StepStatus.Success;
        }

        public void Validate()
        {
            Status = StepStatus.Pending;
            Log = "";
            AddLog("--- Проверка интеграции Животного ---");

            // 1. Папки
            EnsureFolder("Assets/_Project/Art/Animals/Materials");
            EnsureFolder("Assets/_Project/Prefabs/Animals");

            // 2. Настройка FBX импорта с анимацией
            ValidateAndConfigureFBXImport();

            // 3. Валидация материала
            ValidateOrCreateMaterial();
            
            // 4. Валидация Animator Controller
            ValidateOrCreateAnimatorController();

            // 5. Проверка префаба
            ValidateAnimalPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            if(Status != StepStatus.Error) AddLog("Проверка животного завершена.");
        }

        public void ApplyFix()
        {
            if (Status != StepStatus.Error) return;
            AutoFix = true;
            Log = "";
            AddLog("--- Запуск автоисправлений для животного ---");
            Validate();
            AutoFix = false;
        }

        public void Reset() => Status = StepStatus.Pending;

        // --- FBX Импорт ---
        private void ValidateAndConfigureFBXImport()
        {
            if (!File.Exists(AnimalFbxPath))
            {
                AddLog($"FBX животного не найден: {AnimalFbxPath}", true);
                return;
            }

            var importer = AssetImporter.GetAtPath(AnimalFbxPath) as ModelImporter;
            if (importer == null) return;

            bool needsReimport = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                AddLog("Тип анимации не Generic. Настраиваем...");
                if (AutoFix) { importer.animationType = ModelImporterAnimationType.Generic; needsReimport = true; }
            }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                AddLog("Отключаем импорт материалов из FBX...");
                if (AutoFix) { importer.materialImportMode = ModelImporterMaterialImportMode.None; needsReimport = true; }
            }
            if (needsReimport)
            {
                importer.SaveAndReimport();
                AddLog("Настройки FBX для животного обновлены.");
            }
            else AddLog("Настройки FBX для животного корректны.");
        }

        // --- Материал ---
        private void ValidateOrCreateMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AnimalMaterialPath);
            if (mat == null && AutoFix)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, AnimalMaterialPath);
                AddLog("Создан материал для животного (URP Lit).");
            }
            else if (mat != null && mat.shader.name.Contains("Universal Render Pipeline")) 
                AddLog("Материал животного настроен (URP Lit).");
            else AddLog("Материал животного отсутствует или не URP Lit.", true);
            
            // TODO: Привязка текстур как в WeaponIntegrator
        }

        // --- Animator Controller с Blend Tree ---
        private void ValidateOrCreateAnimatorController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
            if (controller == null && AutoFix)
            {
                AddLog("Создаю Animator Controller с Blend Tree...");
                EnsureFolder(Path.GetDirectoryName(AnimatorControllerPath));
                controller = AnimatorController.CreateAnimatorControllerAtPath(AnimatorControllerPath);

                if (controller == null)
                {
                    AddLog("Не удалось создать Animator Controller!", true);
                    return;
                }

                Undo.RegisterCreatedObjectUndo(controller, "Create Anim Controller");

                // Добавляем параметр Speed
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

                // Получаем корневой стейт-машин
                var rootStateMachine = controller.layers[0].stateMachine;

                // Создаём Blend Tree для Locomotion
                var locomotionBlendTree = new BlendTree
                {
                    name = "Locomotion",
                    blendParameter = "Speed",
                    children = new[]
                    {
                        new ChildMotion { motion = CreatePlaceholderClip(controller, "Idle"), threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = CreatePlaceholderClip(controller, "Walk"), threshold = 0.5f, timeScale = 1f },
                        new ChildMotion { motion = CreatePlaceholderClip(controller, "Run"), threshold = 1f, timeScale = 1f }
                    }
                };

                // Сохраняем Blend Tree как ассет
                AssetDatabase.AddObjectToAsset(locomotionBlendTree, controller);

                // Создаём стейт для Blend Tree и делаем его дефолтным
                var locomotionState = rootStateMachine.AddState("LocomotionState");
                locomotionState.motion = locomotionBlendTree;
                rootStateMachine.defaultState = locomotionState;

                AddLog("Animator Controller с Blend Tree для Locomotion создан.");
            }
            else if (controller != null)
            {
                // Простая проверка
                AddLog(controller.parameters.Length > 0 ? "Animator Controller найден и не пуст." : "Предупреждение: Animator Controller пуст.", controller.parameters.Length == 0);
            }
            else AddLog("Animator Controller отсутствует.", true);
        }

        private AnimationClip CreatePlaceholderClip(AnimatorController controller, string clipName)
        {
            // Создаём пустой клип внутри контроллера
            var clip = new AnimationClip { name = clipName };
            AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        // --- Префаб ---
        private void ValidateAnimalPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnimalPrefabPath);
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AnimalFbxPath);

            if (prefab == null && AutoFix && fbxAsset != null)
            {
                AddLog("Создаю префаб животного из FBX...");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                Undo.RegisterCreatedObjectUndo(instance, "Create Animal Prefab");

                // Добавляем/проверяем Animator
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
                if (controller != null) animator.runtimeAnimatorController = controller;
                else AddLog("Не удалось найти контроллер для привязки!", true);

                // Назначаем материал на SkinnedMeshRenderer
                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                if (renderer != null)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(AnimalMaterialPath);
                    if (mat != null) renderer.sharedMaterial = mat;
                    else AddLog("Материал для рендерера не найден.", true);
                }
                else AddLog("SkinnedMeshRenderer не найден в FBX.", true);

                EnsureFolder(Path.GetDirectoryName(AnimalPrefabPath));
                PrefabUtility.SaveAsPrefabAsset(instance, AnimalPrefabPath);
                Object.DestroyImmediate(instance);
                AddLog("Префаб животного создан.");
            }
            else if (prefab != null)
            {
                var anim = prefab.GetComponent<Animator>();
                AddLog(anim != null ? "Префаб животного OK, Animator присутствует." : "Предупреждение: Animator отсутствует на префабе!", anim == null);
            }
            else AddLog("Префаб животного отсутствует.", true);
        }

        private void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            if (!AutoFix) { AddLog($"Папка не найдена: {path}", true); return; }
            
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}