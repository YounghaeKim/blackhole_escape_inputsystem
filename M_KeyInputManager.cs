using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.Users;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using static UnityEngine.InputSystem.InputAction;

public class M_KeyInputManager : MonoBehaviour
{
    [SerializeField] InputSystemUIInputModule uiInput;
    [SerializeField] [Tooltip("기본적으로 제공하는 ui버튼에 추가적으로 사용")] InputSystemUIInputAddModule uiInput_addedButton;
    [SerializeField] GameObject uiKeyInfo;
    [SerializeField] GraphicRaycaster raycaster;
    [Header("게임 패드 사용 키 액션")]
    public InputActionAsset uiPadAction;
    [Header("게임 패드 비사용 키 액션")]
    public InputActionAsset uiNonePadAction;

    //키를 동시에 안눌리게 관리하려면 queue써라
    [Header("키")]
    [SerializeField] Classes.KeyBindData[] keyBinds = null;
    [SerializeField] [ReadOnly] bool isUIKeyBlock = false;//키입력을 받지 않는 상태
    [SerializeField] [ReadOnly] InputActionAsset inputAction;

    [ReadOnly] [SerializeField] List<S_UIKeyUser> uiKeyUserList = new List<S_UIKeyUser>();//키유저 리스트
    [ReadOnly] [SerializeField] List<S_KeyText> nowSceneKeyTexts = new List<S_KeyText>();//현재 키텍스트들 키가 바뀔때 바뀐거 알려줘야함

    List<PlayerInput> beforeInputs = new List<PlayerInput>();//새로운 UI키 유저가 추가될 때 그 이전에 UI키 사용하던 유저들 저장
    List<S_PadButtonUI> padButtonUIs = new List<S_PadButtonUI>();//패드가 접속되었을 때 UI를 수정해야할 UI들

    //패드 진동
    float currentMortorPower;//현재 진동파워
    float mortorWeakSpd = 4f;//진동 약해지는 속도 더이상 추가되지 않으면 0.5초 진동하겟지
    Coroutine NowMotor;//현재 패드 진동 중

    [ReadOnly] [SerializeField] static Enums.GamePadType currentGamepad = Enums.GamePadType.None;
    static S_Title title;

    bool escDowned;
    static float vibrationPower;

    public Classes.KeyBindData[] KeyBinds { get => keyBinds; set => keyBinds = value; }
    public List<S_KeyText> NowSceneKeyTexts { get => nowSceneKeyTexts; set => nowSceneKeyTexts = value; }
    public static S_Title Title { get => title; set => title = value; }
    public List<S_PadButtonUI> PadButtonUIs { get => padButtonUIs; set => padButtonUIs = value; }
    public static Enums.GamePadType CurrentGamepad { get => currentGamepad; set => currentGamepad = value; }

    private void Awake()
    {
        S_KeyText.KeyMng = this;
        S_ControlConfig.KeyMng = this;

        InputSystem.onDeviceChange += GamePadChange;//게임패드가 연결되거나 바뀌면 호출
    }

    IEnumerator Start()
    {
        yield return null;
        GamePadChange(null, InputDeviceChange.Added);//게임 실행 시 게임 패드 인식
        SetVibrationPower();//게임 시작시 옵션에서 진동 파워 가져오기

        //게임패드 사용 여부에 따라서 사용할 inputactionasset을 변경한다.
        if (M_Manager.instance.UseGamepad)
        {
            uiInput.actionsAsset = uiPadAction;
            uiInput_addedButton.Init(uiPadAction);
        }
        else
        {
            uiInput.actionsAsset = uiNonePadAction;
            uiInput_addedButton.Init(uiNonePadAction);
        }
    }

    /// <summary>
    /// 키 입력 막기
    /// </summary>
    public void KeyInputBlock()
    {
        for (int i = 0; i < PlayerInput.all.Count; i++)//ui키를 사용하는 유저가 있으면 키 입력 막기 0번은 uiinput이다
        {
            beforeInputs.Add(PlayerInput.all[i]);
            PlayerInput.all[i].DeactivateInput();
        }
    }

    /// <summary>
    /// 키 입력 다시 시작
    /// </summary>
    public void KeyInputReStart()
    {
        //씬 바꿀 떄 이미 다 삭제되어있따.

        foreach (var beforeInput in beforeInputs)
        {
            if (beforeInput.gameObject)
                beforeInput.ActivateInput();
        }

        PlayerInputClear();
    }

    /// <summary>
    /// 키 사용자들 리스트를 초기화한다. 씬이 바뀔 때 
    /// </summary>
    public void PlayerInputClear()
    {
        beforeInputs.Clear();
    }

    /// <summary>
    /// 씬 바뀔떄만 사용해라
    /// </summary>
    public void UIKeyInputBlock()
    {
        isUIKeyBlock = true;
    }

    /// <summary>
    /// 씬 바뀔떄만 사용해라
    /// </summary>
    public void UIKeyInputReStart()
    {
        isUIKeyBlock = false;
    }

    public void GetUIKeyAuth(S_UIKeyUser newUser)//키의 사용권 얻어오기
    {
        S_SelectHighlight.NavigationAudioOffOnce();//팝업이 열릴때는 네비게이션 소리가 나면 안된다.

        if (newUser.Pause)//새 유저가 게임을 멈추면
            M_Manager.instance.GamePause();//게임 멈추기

        //이전 유저의 마지막 셀렉트를 저장한다.
        if (uiKeyUserList.Count != 0)
        {
            uiKeyUserList[uiKeyUserList.Count - 1].LastSelected = EventSystem.current.currentSelectedGameObject;
        }
        else//이전 유저가 없었다면
        {
            SetUIKey();
        }

        //새 유저 추가
        uiKeyUserList.Add(newUser);

        //ui하이라이트 설정
        if (newUser.HighlightNotUse)
        {
            M_Manager.instance.UiHighlight.SelectHighLightDisuse();
        }
        else
        {
            M_Manager.instance.UiHighlight.SelectHighLightUse();
        }

        KeyInputBlock();

        //패드 연결 안되어있으면 마우스 보이기
        if (!newUser.CursorUnVisible)
            M_Manager.instance.MouseManager.SetCursorVisible();
        else
            M_Manager.instance.MouseManager.SetCursorUnVisible();

        //ui키 설명
        uiKeyInfo.gameObject.SetActive(newUser.KeyInfo);

        //새 유저의 startSelect
        if (newUser.StartSelect)
            newUser.StartSelect.Select();
    }

    public void ReleaseUIKeyAuth(S_UIKeyUser releaseUser)//키의 사용권 되돌리기
    {
        S_SelectHighlight.NavigationAudioOffOnce();//팝업이 열릴때는 네비게이션 소리가 나면 안된다.

        //릴리즈 하는 놈이 현재 권한자인가
        bool isCurrentAuth = uiKeyUserList[uiKeyUserList.Count - 1] == releaseUser;

        uiKeyUserList.Remove(releaseUser);//리스트에서 삭제

        //현재 권한자가 아니면 이 다음줄은 할 필요 없다.
        if (!isCurrentAuth)
            return;
        else if (uiKeyUserList.Count != 0)//사용자가 남아있다면
        {
            //남은 사용자 중 하나라도 게임을 멈췄다면
            bool pauseBeforeUser = false;
            foreach (var beforeUser in uiKeyUserList)
            {
                if (beforeUser.Pause)//남은 사용자 중 하나라도 게임을 멈췄다면
                {
                    pauseBeforeUser = true;
                    break;
                }
            }
            if (pauseBeforeUser)
                M_Manager.instance.GamePause();//게임 멈추기
            else
                M_Manager.instance.GameResume();//게임 다시 시간 가기



            //남은 사용자 중 최상위 사용자의 선택 오브젝트 다시 선택
            if (uiKeyUserList[uiKeyUserList.Count - 1].LastSelected)
            {
                uiKeyUserList[uiKeyUserList.Count - 1].LastSelected.GetComponent<Selectable>().Select();

                //ui키 설명
                uiKeyInfo.gameObject.SetActive(uiKeyUserList[uiKeyUserList.Count - 1].KeyInfo);
            }
        }
        else//사용자가 안남았으면
        {
            M_Manager.instance.GameResume();//게임 다시 시간 가기
            M_Manager.instance.UiHighlight.SelectHighLightDisuse();

            UnSetUIKey();

            //ui키 사용자가 없으면 커서 보이기 리셋
            M_Manager.instance.MouseManager.SetCursorReset();

            //ui키 설명 끄기
            uiKeyInfo.gameObject.SetActive(false);

            KeyInputReStart();
        }
    }

    /// <summary>
    /// cancel과 menu키들은 switch로 둘 중 하나만 사용한다.
    /// </summary>
    public void OnMenu(CallbackContext callback)
    {
        if (isUIKeyBlock)//ui키 사용자가 있으면 메뉴키를 사용하지 않는다.
            return;

        if (uiKeyUserList.Count != 0 && uiKeyUserList[uiKeyUserList.Count - 1].BlockMenu)//메뉴키가 현재 막혀있다면
            return;

        if (callback.control.name == "escape")
        {
            EscKey();//esc는 따로 관리
            return;
        }

        if (title)//타이틀이면 세팅 열고
        {
            if (uiKeyUserList.Count == 1)//타이틀만 ui키를 사용하고 있으면
            {
                M_Manager.instance.PopupManager.SettingPopup.OnSetting();
                M_Manager.instance.AudioManager.PlayButtonAudio(null);
            }
            else if (uiKeyUserList[uiKeyUserList.Count - 1] == M_Manager.instance.PopupManager.SettingPopup)//세팅만 켜져있다면 세팅을 끈다.
            {
                uiKeyUserList[uiKeyUserList.Count - 1].OnCancelKey();
            }
        }
        else//타이틀 이외 => 메뉴가 안켜져있으면 킨다.
        {
            if (!M_Manager.instance.PopupManager.MenuPopup.gameObject.activeSelf)
            {
                M_Manager.instance.PopupManager.MenuPopup.gameObject.SetActive(true);
                M_Manager.instance.AudioManager.PlayButtonAudio(null);
            }
            else if (uiKeyUserList[uiKeyUserList.Count - 1] == M_Manager.instance.PopupManager.SettingPopup ||
                uiKeyUserList[uiKeyUserList.Count - 1] == M_Manager.instance.PopupManager.MenuPopup)//세팅 or 메뉴면 끈다.
            {
                uiKeyUserList[uiKeyUserList.Count - 1].OnCancelKey();
            }
        }
    }



    void OnCancel(CallbackContext callback)
    {
        if (isUIKeyBlock)//ui키 사용자가 있으면 메뉴키를 사용하지 않는다.
            return;

        if (callback.control.name == "escape")
        {
            EscKey();//esc는 따로 관리
            return;
        }

        if (uiKeyUserList.Count != 0)
        {
            uiKeyUserList[uiKeyUserList.Count - 1].OnCancelKey();
        }
    }

    void OnCancelUp(CallbackContext callback)
    {
        if (isUIKeyBlock)//ui키 사용자가 있으면 메뉴키를 사용하지 않는다.
            return;

        if (uiKeyUserList.Count != 0)
        {
            uiKeyUserList[uiKeyUserList.Count - 1].OnCancelKeyUp();
        }
    }

    /// <summary>
    /// uikey는 항상 사용하지 않는다.
    /// </summary>
    private void SetUIKey()
    {
        //ui키가 눌렸을 떄 반응
        uiInput.cancel.action.performed += OnCancel;
        uiInput.cancel.action.canceled += OnCancelUp;
    }

    private void UnSetUIKey()
    {
        //ui키가 눌렸을 떄 반응
        uiInput.cancel.action.performed -= OnCancel;
        uiInput.cancel.action.canceled -= OnCancelUp;
    }

    IEnumerator EscapeKeyBlock()
    {
        escDowned = true;
        yield return null;
        yield return null;
        escDowned = false;
    }

    /// <summary>
    /// esc는 캔슬과 메뉴키가 같다.
    /// </summary>
    private void EscKey()
    {
        //항상 두 번씩 실행된다. 취소와 메뉴로
        //한 프레임에 한 번만 실행되어야 한다.
        if (escDowned)
            return;

        StartCoroutine(EscapeKeyBlock());

        if (title)//타이틀이면 세팅 열고
        {

            if (uiKeyUserList.Count == 1)//타이틀만 ui키를 사용하고 있으면 메뉴를 키고
            {
                M_Manager.instance.PopupManager.SettingPopup.OnSetting();
                M_Manager.instance.AudioManager.PlayButtonAudio(null);
                return;
            }
        }
        else//타이틀 이외 => 메뉴가 안켜져있으면 킨다.
        {
            if ((uiKeyUserList.Count == 0 || !uiKeyUserList[uiKeyUserList.Count - 1].BlockMenu) && //메뉴키가 막히지 않으면
                !M_Manager.instance.PopupManager.MenuPopup.gameObject.activeSelf)//메뉴가 켜져있지 않으면
            {
                M_Manager.instance.PopupManager.MenuPopup.gameObject.SetActive(true);
                M_Manager.instance.AudioManager.PlayButtonAudio(null);
                return;
            }
        }


        //여까지 왔으면 취소키
        uiKeyUserList[uiKeyUserList.Count - 1].OnCancelKey();
    }

    /// <summary>
    /// UI메뉴에서
    /// </summary>
    /// <param name="callback"></param>
    public void OnPrev(CallbackContext callback)
    {
        if (isUIKeyBlock)//ui키가 막혀있다면 사용하지 않는다.
            return;
        if (!isUIKeyBlock && uiKeyUserList.Count != 0)//ui를 사용 중이면
            uiKeyUserList[uiKeyUserList.Count - 1].Prev();
    }

    public void OnNext(CallbackContext callback)
    {
        if (isUIKeyBlock)//ui키가 막혀있다면 사용하지 않는다.
            return;
        if (!isUIKeyBlock && uiKeyUserList.Count != 0)//ui를 사용 중이면
            uiKeyUserList[uiKeyUserList.Count - 1].Next();
    }


    /// <summary>
    /// 게임패드에 변화가 있다면, connect, disconnect
    /// </summary>
    public void GamePadChange(InputDevice device, InputDeviceChange change)
    {
        if(!M_Manager.instance.UseGamepad)//게임패드를 사용하지 않는 게임이면 return
            return;

        if (Gamepad.current == null && Joystick.current == null)//구형 패드는 인식 못한다 플스와 엑박만
        {
            //Debug.Log("패드 off");
            //구형 패드는 여기로 넘어온다 Gamepad가 아닌 joystick으로 들어간다.
            //어차피 구형패드는 버튼이 다 다르다. 맟출수 없어
            currentGamepad = Enums.GamePadType.None;//게임패드 연결안됨
        }
        else
        {
            //https://aidantakami.com/2021/02/02/detecting-the-players-controller-type-with-the-unity-input-system/
            //처음 연결된 패드
            if (Gamepad.current != null && Gamepad.current.description.manufacturer == "Sony Interactive Entertainment")//ps패드
            {
                currentGamepad = Enums.GamePadType.PS;
            }
            else//xbox패드 및 나머지
            {
                currentGamepad = Enums.GamePadType.XB;
            }
        }

        foreach (var padButtonUI in padButtonUIs)
        {
            padButtonUI.GamePadChange();
        }

        //패드 연결됨 마우스에 알림
        if (uiKeyUserList.Count != 0)//ui가 켜져있다면
        {

            if (currentGamepad != Enums.GamePadType.None)//패드가 연결되어있으면
            {
                M_Manager.instance.MouseManager.SetCursorUnVisible();//마우스 끄기
            }
            else
            {
                M_Manager.instance.MouseManager.SetCursorVisible();//마우스 끄기
            }
        }
        else//ui가 켜져있지 않다면
        {
            if (M_Manager.instance && M_Manager.instance.MouseManager)
                M_Manager.instance.MouseManager.SetCursorReset();//마우스 초기화
        }
    }

    /// <summary>
    /// 키 초기화, 키 언바인드, 키 뉴바인드에서 호출, 키 텍스트들에 키가 바뀌었음을 알림
    /// </summary>
    public void KeyChanged()
    {
        foreach (var keyText in nowSceneKeyTexts)
        {
            keyText.SetKeyText();
        }
    }


    /// <summary>
    /// 게임패드 진동 파워 추가
    /// </summary>
    public void AddMotorPower(float power)
    {
        if (Gamepad.current == null || vibrationPower == 0)//게임패드가 없으면 무시해라
            return;

        currentMortorPower += power;
        if (currentMortorPower > vibrationPower)
            currentMortorPower = vibrationPower;
        else
        {
            currentMortorPower *= vibrationPower;
        }

        if (NowMotor == null)
        {
            NowMotor = StartCoroutine(NowMotoring());
        }
    }

    /// <summary>
    /// 진동 파워 설정
    /// </summary>
    public static void SetVibrationPower()
    {
        vibrationPower = M_Manager.instance.ConfigManager.GetCurrentChangable().Vibration / 10f;
    }

    /// <summary>
    /// 진동 멈추기
    /// </summary>
    public void MotorStop()
    {
        currentMortorPower = 0;
    }

    /// <summary>
    /// 현재 진동 중
    /// </summary>
    IEnumerator NowMotoring()
    {
        while (currentMortorPower > 0)
        {
            Gamepad.current.SetMotorSpeeds(currentMortorPower, currentMortorPower);
            currentMortorPower -= mortorWeakSpd * Time.unscaledDeltaTime;//점점 약해짐
            yield return M_Manager.WaitRealFix;
        }
        currentMortorPower = 0;
        Gamepad.current.SetMotorSpeeds(currentMortorPower, currentMortorPower);
        NowMotor = null;
    }


}