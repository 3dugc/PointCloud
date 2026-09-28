#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bujiaban.PointCloud.Immersal;
using UnityEditor;
using UnityEngine;

namespace Bujiaban.PointCloud.Simulation
{
    public sealed class PointCloudSimulationScene : MonoBehaviour
    {
        [SerializeField] private ImmersalLocalizationProfile _rokidProfile;
        [SerializeField] private ImmersalLocalizationProfile _iosProfile;
        private SimulationBackend _backend;
        private PointCloudLocalizer _localizer;
        private CancellationTokenSource _cancellation;
        private Transform _placed;
        private Transform _candidate;
        private Transform _uncorrected;
        private Camera _viewCamera;
        private Pose _deliveredPose;
        private bool _hasDeliveredPose;
        private bool _quantitative = true;
        private int _experimentScenario;
        private float _injectCentimeters = 8f, _injectDegrees = 3f;
        private Vector2 _controlScroll;
        private Transform _observer;
        private int _platform;
        private int _scenario;
        private int _request;
        private int _lastObservedAttempt = -1;
        private string _lastStatus;
        private string _message = "选择设备参数和测试情况，然后点击开始。";
        private readonly List<string> _events = new List<string>();
        private Vector2 _scroll;
        private Font _font;
        private readonly List<Material> _materials = new List<Material>();
        internal SimulationBackend Backend => _backend;

        private void Start()
        {
            _backend = gameObject.AddComponent<SimulationBackend>();
            _localizer = gameObject.AddComponent<PointCloudLocalizer>();
            var serialized = new SerializedObject(_localizer);
            var backends = serialized.FindProperty("_backends");
            backends.arraySize = 1;
            backends.GetArrayElementAtIndex(0).objectReferenceValue = _backend;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _font = Font.CreateDynamicFontFromOSFont(
                Application.platform == RuntimePlatform.OSXEditor ? "Arial Unicode MS" : "Microsoft YaHei", 16);
            CreateView();
        }

        internal void StartSimulation(int platform, int scenario)
        {
            _quantitative = false;
            _scenario = scenario;
            _backend.SelectedScenario = (Scenario)scenario;
            _backend.ExperimentScenario = null;
            StartRequest(platform);
        }

        internal void StartExperiment(int platform, DriftScenario scenario)
        {
            _quantitative = true;
            _experimentScenario = (int)scenario;
            _backend.SelectedScenario = Scenario.Normal;
            _backend.ExperimentScenario = scenario;
            StartRequest(platform);
        }

        private void StartSelected()
        {
            if (_quantitative) StartExperiment(_platform, (DriftScenario)_experimentScenario);
            else StartSimulation(_platform, _scenario);
        }

        private void StartRequest(int platform)
        {
            _platform = platform;
            _backend.Profile = platform == 0 ? _rokidProfile : _iosProfile;
            _backend.Paused = false;
            int request = ++_request;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _hasDeliveredPose = false;
            _placed.gameObject.SetActive(false);
            _uncorrected.gameObject.SetActive(false);
            _events.Clear();
            _lastObservedAttempt = -1;
            _lastStatus = null;
            _message = "开始定位；完成首次确认后可注入漂移。";
            _ = RunAsync(request, cancellation);
        }

        private async Task RunAsync(int request, CancellationTokenSource cancellation)
        {
            using (var stream = new MemoryStream(new byte[] { 0 }))
            {
                try
                {
                    await _localizer.TrackAsync("simulation", stream, pose =>
                    {
                        if (request != _request || this == null) return;
                        _hasDeliveredPose = true;
                        _deliveredPose = pose;
                    }, cancellation.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    if (request == _request) _message = "测试异常：" + exception.Message;
                    Debug.LogException(exception);
                }
                finally
                {
                    if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                    cancellation.Dispose();
                }
            }
        }

        internal void StopSimulation()
        {
            _cancellation?.Cancel();
            _message = "已请求停止，等待模拟后端结束。";
        }

        private void Update()
        {
            var model = _backend?.Model;
            if (model == null) return;
            var experiment = model.Experiment;
            Pose candidate = experiment == null ? model.Candidate : experiment.CandidateWorldPose;
            Pose placed = experiment == null ? _deliveredPose : experiment.ToWorld(_deliveredPose);
            Pose observer = experiment == null ? model.Camera :
                experiment.ToWorld(experiment.CameraPose(model.Clock));
            SetDisplayPose(_candidate, candidate, .55f);
            SetDisplayPose(_placed, placed, .18f);
            SetDisplayPose(_observer, observer, 0f);
            _placed.gameObject.SetActive(_hasDeliveredPose);
            _uncorrected.gameObject.SetActive(experiment?.HasBaseline == true);
            if (experiment?.HasBaseline == true)
                SetDisplayPose(_uncorrected, experiment.UncorrectedWorldPose, .06f);
            // Zoom on centimetre-scale offsets while retaining large-error visibility.
            float extent = Mathf.Max(placed.position.magnitude, candidate.position.magnitude);
            if (experiment?.HasBaseline == true)
                extent = Mathf.Max(extent, experiment.UncorrectedWorldPose.position.magnitude);
            _viewCamera.orthographicSize = Mathf.Max(.65f, extent * 1.35f + .3f);
            if (_lastObservedAttempt == model.Attempts && _lastStatus == model.Status) return;
            _lastObservedAttempt = model.Attempts;
            _lastStatus = model.Status;
            _events.Add($"t={model.Clock:0.0}s  {model.Status}  一致观察 {model.Gate.StableCount} 次（至少 {model.Gate.RequiredSamples} 次）");
            if (_events.Count > 100) _events.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private static readonly string[] ExperimentLabels =
            { "手动阶跃（可重复）", "持续漂移 30 秒", "持续漂移＋噪声", "短暂错误匹配", "持续错误匹配" };
        private static readonly string[] ExperimentDescriptions =
        {
            "首次定位后注入指定厘米数和偏航角。可连续注入，检查多次矫正。",
            "自动注入缓慢平移和旋转漂移，30 秒后停止；观察误差是否收敛。",
            "相同漂移叠加确定性的毫米级、角度测量噪声，观察残余误差。",
            "短暂提供高质量但错误的定位结果，检查是否发生错误移动。",
            "持续提供一致错误结果，展示门禁可能接受错误位置的能力边界。"
        };

        private void OnGUI()
        {
            if (_backend == null) return;
            var previousMatrix = GUI.matrix;
            var previousFont = GUI.skin.font;
            bool previousWrap = GUI.skin.label.wordWrap;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 800f, 1));
            GUI.skin.font = _font;
            GUI.skin.label.wordWrap = true;
            GUILayout.BeginArea(new Rect(12, 12, 358, 776), GUI.skin.box);
            _controlScroll = GUILayout.BeginScrollView(_controlScroll);
            GUILayout.Label("持续定位 · 量化验证");
            _quantitative = GUILayout.Toolbar(_quantitative ? 0 : 1,
                new[] { "误差对照", "原有流程案例" }) == 0;
            _platform = GUILayout.Toolbar(_platform, new[] { "Rokid 参数", "iOS 参数" });
            EditorGUILayout.ObjectField("下一轮配置", _platform == 0 ? _rokidProfile : _iosProfile,
                typeof(ImmersalLocalizationProfile), false);
            if (_quantitative)
            {
                _experimentScenario = GUILayout.SelectionGrid(_experimentScenario, ExperimentLabels, 1);
                GUILayout.Label(ExperimentDescriptions[_experimentScenario]);
            }
            else
                _scenario = GUILayout.SelectionGrid(_scenario, SimulationModel.Labels, 2);
            GUILayout.Label("选择变化后点击开始生效。建议 1 倍观察平滑过程。");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("开始 / 重新开始", GUILayout.Height(30))) StartSelected();
            if (GUILayout.Button("停止", GUILayout.Height(30))) StopSimulation();
            GUILayout.EndHorizontal();
            var model = _backend.Model;
            if (_quantitative)
            {
                GUILayout.Space(6);
                GUILayout.Label($"注入平移：{_injectCentimeters:0} cm");
                _injectCentimeters = Mathf.Round(GUILayout.HorizontalSlider(_injectCentimeters, 0, 100));
                GUILayout.Label($"注入偏航：{_injectDegrees:0.0}°");
                _injectDegrees = Mathf.Round(GUILayout.HorizontalSlider(_injectDegrees, 0, 15) * 10) / 10f;
                GUI.enabled = model?.Running == true && model.Experiment?.HasBaseline == true &&
                    (_injectCentimeters > 0 || _injectDegrees > 0);
                if (GUILayout.Button("注入漂移（可重复）", GUILayout.Height(28)))
                {
                    model.Experiment.InjectDrift(_injectCentimeters, _injectDegrees);
                    _events.Add($"t={model.Clock:0.0}s  注入 {_injectCentimeters:0} cm / {_injectDegrees:0.0}° 漂移");
                    _scroll.y = float.MaxValue;
                }
                GUI.enabled = true;
                if (model?.Experiment?.HasBaseline != true) GUILayout.Label("等待首次定位后才能注入。");
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_backend.Paused ? "继续" : "暂停")) _backend.Paused = !_backend.Paused;
            if (GUILayout.Button("跟踪丢失")) model?.SetTracking(false);
            if (GUILayout.Button("跟踪恢复")) model?.SetTracking(true);
            GUILayout.EndHorizontal();
            GUILayout.Label($"播放速度：{_backend.Speed:0} 倍");
            _backend.Speed = Mathf.Round(GUILayout.HorizontalSlider(_backend.Speed, 1, 10));
            if (model != null)
            {
                string phase = !model.Running ? "已停止" : !model.Tracking ? "跟踪丢失" :
                    model.Reacquiring ? "严格重定位" : model.Smoothing ? "正在平滑矫正" :
                    model.HasPose ? "低频维护" : "首次定位";
                GUILayout.Label($"运行：{_backend.Profile.name} · {phase}");
                GUILayout.Label($"模拟时间 {model.Clock:0.0}s · 尝试 {model.Attempts} 次");
                GUILayout.Label($"一致观察 {model.Gate.StableCount} 次（至少 {model.Gate.RequiredSamples} 次）· 门禁确认 {model.Confirmations} 次");
                GUILayout.Label($"平滑修正 {model.Corrections} · 重定位 {model.Reacquisitions} · 拒绝 {model.Rejections}");
                GUILayout.Label(model.Status);
            }
            GUILayout.Space(6);
            if (GUILayout.Button("运行双端自动检查")) RunChecks();
            if (GUILayout.Button("连续发起两次（检查替换）")) { StartSelected(); StartSelected(); }
            GUILayout.Label($"任务启动 {_backend.Started} / 结束 {_backend.Released} / 并发 {_backend.Concurrent}");
            GUILayout.Label(_message);
            GUILayout.Label("测试使用合成观测，不代表真实照片识别或真机定位精度。");
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            DrawMetrics(model);
            DrawPlot(new Rect(386, 431, 435, 173), model?.Experiment, false);
            DrawPlot(new Rect(833, 431, 435, 173), model?.Experiment, true);
            GUILayout.BeginArea(new Rect(386, 616, 882, 172), GUI.skin.box);
            GUILayout.Label("本轮记录");
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string entry in _events) GUILayout.Label(entry);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.skin.font = previousFont;
            GUI.skin.label.wordWrap = previousWrap;
            GUI.matrix = previousMatrix;
        }

        private void DrawMetrics(SimulationModel model)
        {
            GUILayout.BeginArea(new Rect(386, 12, 882, 183), GUI.skin.box);
            var experiment = model?.Experiment;
            bool quantitative = experiment != null || model == null && _quantitative;
            GUILayout.Label(quantitative ? "物理真值固定 · 青色开启矫正 · 橙色保留首次位姿 · 黄色测量" :
                "原有流程演示：白色仅为零点参考，不代表纠偏目标");
            if (experiment?.HasBaseline == true)
            {
                GUILayout.Label($"位置误差    开启 {experiment.PositionErrorCm:0.00} cm    关闭 {experiment.UncorrectedPositionErrorCm:0.00} cm" +
                    $"       角度误差    开启 {experiment.RotationErrorDegrees:0.00}°    关闭 {experiment.UncorrectedRotationErrorDegrees:0.00}°");
                GUILayout.Label($"曲线采样点位置 RMS    开启 {experiment.PositionRmsCm:0.00} cm    关闭 {experiment.UncorrectedPositionRmsCm:0.00} cm");
                GUILayout.Label(experiment.RecoverySeconds < 0 ? "本次扰动恢复耗时：尚未达到稳定恢复条件" :
                    $"本次扰动首次稳定恢复耗时：{experiment.RecoverySeconds:0.0} 秒（模拟时间）");
                GUILayout.Label("恢复参考：扰动停止后，已验证跟踪且误差 ≤ 1 cm / 0.5°，连续保持 2 秒。");
            }
            else GUILayout.Label(quantitative ? "完成首次定位后建立两组基线。数字与曲线都使用物理世界误差，不使用追踪坐标位移。" :
                "这里验证门禁、候选响应与任务生命周期。选择“误差对照”重新开始，才能量化漂移纠正效果。");
            GUILayout.Label(quantitative ? "白色十字为固定物理真值；场景自动缩放，数值不放大。长条表示朝向。" :
                "青色为输出，黄色为候选。候选变化本身不代表真实误差改善。");
            GUILayout.EndArea();
        }

        private static void DrawPlot(Rect rect, CorrectionExperiment experiment, bool rotation)
        {
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 10, rect.y + 5, rect.width - 20, 23),
                rotation ? "角度误差（°）：青色开启 / 橙色关闭" : "位置误差（cm）：青色开启 / 橙色关闭");
            var samples = experiment?.Samples;
            if (samples == null || samples.Count < 2)
            {
                GUI.Label(new Rect(rect.x + 12, rect.y + 52, rect.width - 24, 40), "等待误差采样…");
                return;
            }
            double start = samples[0].Time, end = samples[samples.Count - 1].Time;
            float ceiling = rotation ? .5f : 1f;
            foreach (var sample in samples)
                ceiling = Mathf.Max(ceiling, rotation ? Mathf.Max(sample.CorrectedDegrees, sample.UncorrectedDegrees) :
                    Mathf.Max(sample.CorrectedCm, sample.UncorrectedCm));
            ceiling *= 1.1f;
            Rect plot = new Rect(rect.x + 45, rect.y + 31, rect.width - 58, rect.height - 57);
            GUI.Label(new Rect(rect.x + 5, plot.y - 7, 42, 20), ceiling.ToString("0.0"));
            GUI.Label(new Rect(rect.x + 14, plot.yMax - 12, 24, 20), "0");
            GUI.Label(new Rect(plot.x, plot.yMax + 1, 75, 20), $"{start:0.0}s");
            GUI.Label(new Rect(plot.xMax - 70, plot.yMax + 1, 70, 20), $"{end:0.0}s");
            var corrected = new Vector3[samples.Count];
            var uncorrected = new Vector3[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                float x = plot.x + plot.width * (float)((sample.Time - start) / Math.Max(.001, end - start));
                corrected[i] = new Vector3(x, plot.yMax - plot.height *
                    (rotation ? sample.CorrectedDegrees : sample.CorrectedCm) / ceiling);
                uncorrected[i] = new Vector3(x, plot.yMax - plot.height *
                    (rotation ? sample.UncorrectedDegrees : sample.UncorrectedCm) / ceiling);
            }
            Handles.BeginGUI();
            Color previous = Handles.color;
            Handles.color = new Color(.4f, .4f, .4f);
            Handles.DrawLine(new Vector3(plot.x, plot.y), new Vector3(plot.x, plot.yMax));
            Handles.DrawLine(new Vector3(plot.x, plot.yMax), new Vector3(plot.xMax, plot.yMax));
            float toleranceY = plot.yMax - plot.height * (rotation ? .5f : 1f) / ceiling;
            Handles.color = new Color(.5f, .65f, .5f);
            Handles.DrawDottedLine(new Vector3(plot.x, toleranceY), new Vector3(plot.xMax, toleranceY), 4);
            Handles.color = new Color(1f, .55f, .15f);
            Handles.DrawAAPolyLine(2f, uncorrected);
            Handles.color = Color.cyan;
            Handles.DrawAAPolyLine(2f, corrected);
            Handles.color = previous;
            Handles.EndGUI();
        }

        private static void SetDisplayPose(Transform target, Pose pose, float height)
        {
            target.SetPositionAndRotation(pose.position + Vector3.up * height, pose.rotation);
        }

        private void RunChecks()
        {
            try
            {
                string report = SimulationChecks.RunAll(_rokidProfile, _iosProfile);
                _message = "双端自动检查通过。完整结果已输出到 Console。";
                Debug.Log(report, this);
            }
            catch (Exception exception)
            {
                _message = "自动检查失败：" + exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private void CreateView()
        {
            var cameraObject = new GameObject("Simulation Camera");
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            _viewCamera = camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .07f, .095f);
            camera.rect = new Rect(386f / 1280f, 1f - 419f / 800f, 882f / 1280f, 212f / 800f);
            camera.orthographic = true;
            camera.orthographicSize = 2.5f;
            camera.transform.position = new Vector3(0, 5, 0);
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            for (int i = -5; i <= 5; i++)
            {
                Box("Grid", new Vector3(i * .5f, -.04f, 0), new Vector3(.01f, .01f, 5), new Color(.15f,.2f,.25f));
                Box("Grid", new Vector3(0, -.04f, i * .5f), new Vector3(5, .01f, .01f), new Color(.15f,.2f,.25f));
            }
            Box("Fixed Physical Truth X", Vector3.zero, new Vector3(.35f,.015f,.012f), Color.white);
            Box("Fixed Physical Truth Z", Vector3.zero, new Vector3(.012f,.015f,.35f), Color.white);
            _placed = Box("Confirmed Pose", Vector3.up * .18f, new Vector3(.045f, .02f, .22f), Color.cyan);
            _placed.gameObject.SetActive(false);
            _uncorrected = Box("Without Continuous Correction", Vector3.up * .06f, new Vector3(.065f,.02f,.25f), new Color(1f,.55f,.15f));
            _uncorrected.gameObject.SetActive(false);
            _candidate = Box("Candidate Pose", Vector3.up * .6f, new Vector3(.03f,.02f,.16f), Color.yellow);
            _observer = Box("Synthetic Camera", new Vector3(0,1.5f,-2), new Vector3(.15f,.1f,.3f), new Color(.8f,.5f,1));
        }

        private Transform Box(string objectName, Vector3 position, Vector3 scale, Color color)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = objectName;
            item.transform.SetParent(transform, false);
            item.transform.position = position;
            item.transform.localScale = scale;
            Destroy(item.GetComponent<Collider>());
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            material.color = color;
            _materials.Add(material);
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item.transform;
        }

        private void OnDisable() => StopSimulation();
        private void OnDestroy()
        {
            foreach (var material in _materials) Destroy(material);
            if (_font != null) Destroy(_font);
        }
    }
}
#endif
