using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局背景音乐与战斗音效。
/// 常驻跨场景，按主页 / 当前章节切歌；小怪攻击与受击走独立音源，保证能互相叠放。
/// </summary>
public sealed class GameAudioManager : MonoBehaviour
{
    public static GameAudioManager Instance { get; private set; }

    [Header("音量")]
    [Range(0f, 1f)] public float musicVolume = 0.32f;
    [Range(0f, 1f)] public float sfxVolume = 0.78f;

    [Header("音频表现")]
    [Tooltip("切歌淡入淡出时长（秒）")]
    public float musicFadeDuration = 0.45f;
    [Tooltip("多段伤害在极短时间内连续结算时，只播放一次受击声")]
    public float hitSoundCooldown = 0.055f;

    private const string MenuMusicPath = "Audio/Music/MainMenu";
    private const string Chapter1MusicPath = "Audio/Music/Chapter1";
    private const string Chapter2MusicPath = "Audio/Music/Chapter2";
    private const string EnemyAttackPath = "Audio/SFX/EnemyAttack";
    private const string EnemyHitBloodPath = "Audio/SFX/EnemyHit_Blood";
    private const string EnemyHitFleshPath = "Audio/SFX/EnemyHit_Flesh";

    private AudioSource musicSource;
    private AudioSource enemyAttackSource;
    private AudioSource enemyHitSource;

    private AudioClip menuMusic;
    private AudioClip chapter1Music;
    private AudioClip chapter2Music;
    private AudioClip enemyAttackClip;
    private AudioClip enemyHitBloodClip;
    private AudioClip enemyHitFleshClip;

    private Coroutine musicTransition;
    private string requestedTrack;
    private bool useFleshHitNext = true;
    private float lastEnemyHitTime = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName == "Start" || sceneName == "MainMenu" || sceneName == "Battle")
            new GameObject("GameAudioManager").AddComponent<GameAudioManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = CreateAudioSource(false);
        enemyAttackSource = CreateAudioSource(false);
        enemyHitSource = CreateAudioSource(false);

        menuMusic = LoadClip(MenuMusicPath);
        chapter1Music = LoadClip(Chapter1MusicPath);
        chapter2Music = LoadClip(Chapter2MusicPath);
        enemyAttackClip = LoadClip(EnemyAttackPath);
        enemyHitBloodClip = LoadClip(EnemyHitBloodPath);
        enemyHitFleshClip = LoadClip(EnemyHitFleshPath);

        EventManager.AddListener<EnemyUnit, CardData>("EnemySkillExecuting", OnEnemySkillExecuting);
        EventManager.AddListener<EnemyUnit, Unit>("EnemyAttacked", OnEnemyAttacked);
        EventManager.AddListener<Unit>("UnitDamaged", OnUnitDamaged);
        SceneManager.sceneLoaded += OnSceneLoaded;

        RefreshForScene(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        EventManager.RemoveListener<EnemyUnit, CardData>("EnemySkillExecuting", OnEnemySkillExecuting);
        EventManager.RemoveListener<EnemyUnit, Unit>("EnemyAttacked", OnEnemyAttacked);
        EventManager.RemoveListener<Unit>("UnitDamaged", OnUnitDamaged);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshForScene(scene);
    }

    private void RefreshForScene(Scene scene)
    {
        if (scene.name == "Start" || scene.name == "MainMenu")
        {
            PlayMusic(menuMusic, "MainMenu");
            return;
        }

        if (scene.name != "Battle") return;

        int chapterIndex = 0;
        RunFlowController flow = RunFlowController.Instance;
        if (flow != null && flow.Progression.Phase == RunPhase.Battle)
            chapterIndex = flow.Progression.ChapterIndex;

        if (chapterIndex <= 0)
            PlayMusic(chapter1Music, "Chapter1");
        else
            PlayMusic(chapter2Music, "Chapter2");
    }

    private void PlayMusic(AudioClip clip, string trackId)
    {
        if (clip == null || trackId == requestedTrack) return;

        requestedTrack = trackId;
        if (musicTransition != null) StopCoroutine(musicTransition);
        musicTransition = StartCoroutine(TransitionMusic(clip));
    }

    private IEnumerator TransitionMusic(AudioClip nextClip)
    {
        if (musicSource.isPlaying)
        {
            float startVolume = musicSource.volume;
            for (float elapsed = 0f; elapsed < musicFadeDuration; elapsed += Time.unscaledDeltaTime)
            {
                musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / musicFadeDuration);
                yield return null;
            }
        }

        musicSource.Stop();
        musicSource.clip = nextClip;
        musicSource.loop = true;
        musicSource.volume = 0f;
        musicSource.Play();

        for (float elapsed = 0f; elapsed < musicFadeDuration; elapsed += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(0f, musicVolume, elapsed / musicFadeDuration);
            yield return null;
        }

        musicSource.volume = musicVolume;
        musicTransition = null;
    }

    private void OnEnemySkillExecuting(EnemyUnit enemy, CardData card)
    {
        if (enemy == null || card == null || !card.IsAttack) return;
        PlayEnemyAttackSfx();
    }

    private void OnEnemyAttacked(EnemyUnit enemy, Unit target)
    {
        if (enemy == null) return;
        PlayEnemyAttackSfx();
    }

    private void OnUnitDamaged(Unit damaged)
    {
        if (!(damaged is EnemyUnit)) return;

        float now = Time.unscaledTime;
        if (now - lastEnemyHitTime < hitSoundCooldown) return;
        lastEnemyHitTime = now;

        AudioClip clip = useFleshHitNext ? enemyHitFleshClip : enemyHitBloodClip;
        useFleshHitNext = !useFleshHitNext;
        if (clip == null) return;

        enemyHitSource.pitch = Random.Range(0.96f, 1.03f);
        enemyHitSource.PlayOneShot(clip, sfxVolume);
    }

    private void PlayEnemyAttackSfx()
    {
        if (enemyAttackClip == null) return;
        enemyAttackSource.pitch = Random.Range(0.97f, 1.03f);
        enemyAttackSource.PlayOneShot(enemyAttackClip, sfxVolume * 0.82f);
    }

    private AudioSource CreateAudioSource(bool loop)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.volume = 1f;
        return source;
    }

    private static AudioClip LoadClip(string resourcePath)
    {
        AudioClip clip = Resources.Load<AudioClip>(resourcePath);
        if (clip == null)
            Debug.LogError("[GameAudio] 缺少音频资源：Assets/Game/Resources/" + resourcePath);
        return clip;
    }
}
