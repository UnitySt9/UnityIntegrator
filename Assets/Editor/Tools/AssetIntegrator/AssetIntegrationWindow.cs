using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Editor.Tools.AssetIntegrator
{
    public class AssetIntegrationWindow : EditorWindow
    {
        private List<IIntegrationStep> steps;
        private Vector2 scrollPos;
        private bool autoFix = false;

        [MenuItem("Tools/Asset Integration Validator")]
        public static void ShowWindow()
        {
            GetWindow<AssetIntegrationWindow>("Asset Validator");
        }

        private void OnEnable()
        {
            // Инициализация шагов. Просто добавьте сюда новый класс, чтобы расширить тулзу.
            steps = new List<IIntegrationStep>
            {
                new WeaponIntegrator(),
                new AnimalIntegrator()
            };
        }

        private void OnGUI()
        {
            GUILayout.Label("Автосборщик-интегратор", EditorStyles.boldLabel);
            autoFix = EditorGUILayout.Toggle("Автоисправление ошибок", autoFix);

            EditorGUILayout.Space(10);
            GUILayout.Label("Шаги для проверки:", EditorStyles.label);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            
            foreach (var step in steps)
            {
                // Рендер чекбокса для каждого шага
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                step.IsEnabled = EditorGUILayout.ToggleLeft(step.StepName, step.IsEnabled);
                
                // Кнопка с визуализацией статуса
                GUI.enabled = step.IsEnabled;
                string btnText = step.Status == StepStatus.Pending ? "Запустить" : $"Статус: {step.Status}";
                if (GUILayout.Button(btnText))
                {
                    step.AutoFix = autoFix;
                    if (step.Status == StepStatus.Error)
                        step.ApplyFix();
                    else
                        step.Validate();
                }
                
                // Лог шага
                if (!string.IsNullOrEmpty(step.Log))
                {
                    var style = new GUIStyle(EditorStyles.textArea)
                    {
                        normal = { textColor = step.Status == StepStatus.Error ? Color.red : Color.white }
                    };
                    GUILayout.TextArea(step.Log, style, GUILayout.ExpandHeight(true));
                }
                GUI.enabled = true;
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);
            // Кнопка "Запустить всё"
            if (GUILayout.Button("Запустить все включенные шаги", GUILayout.Height(30)))
            {
                foreach (var step in steps)
                {
                    if (!step.IsEnabled) continue;
                    step.AutoFix = autoFix;
                    step.Validate();
                }
            }
        }
    }
}