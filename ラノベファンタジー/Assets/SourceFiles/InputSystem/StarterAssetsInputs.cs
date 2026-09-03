using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	public class StarterAssetsInputs : MonoBehaviour
	{
		public static StarterAssetsInputs Instance { get; private set; }

		/// <summary>Esc で切替した UI 操作モード（カーソル表示・カメラ Look 無効）。</summary>
		public bool IsUiInteractionMode { get; private set; }

		public static bool IsUiModeActive => Instance != null && Instance.IsUiInteractionMode;

		[Header("Character Input Values")]
		public Vector2 move;
		public Vector2 look;
		public bool jump;
		public bool sprint;

		[Header("Movement Settings")]
		public bool analogMovement;

		[Header("Mouse Cursor Settings")]
		public bool cursorLocked = true;
		public bool cursorInputForLook = true;



#if ENABLE_INPUT_SYSTEM
		

		
		public void OnMove(InputValue value)
		{
			MoveInput(value.Get<Vector2>());
		}

		public void OnLook(InputValue value)
		{
			if(cursorInputForLook)
			{
				LookInput(value.Get<Vector2>());
			}
		}

		public void OnJump(InputValue value)
		{
			JumpInput(value.isPressed);
		}

		public void OnSprint(InputValue value)
		{
			SprintInput(value.isPressed);
		}
#endif

		private void Awake()
		{
			Instance = this;
			ApplyInteractionMode(gameMode: true);
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}
		}

		private void Update()
		{
			if (WasEscapePressedThisFrame())
			{
				ToggleInteractionMode();
			}
		}

		public void ToggleInteractionMode()
		{
			SetUiInteractionMode(!IsUiInteractionMode);
		}

		public void SetUiInteractionMode(bool uiMode)
		{
			IsUiInteractionMode = uiMode;
			ApplyInteractionMode(!uiMode);
			Debug.Log(uiMode
				? "<color=#CE93D8><b>【入力】</b></color> UI操作モード（マウスで画面クリック可 / Escでゲーム操作に戻る）"
				: "<color=#A5D6A7><b>【入力】</b></color> ゲーム操作モード（カメラ操作可 / EscでUI操作に切替）");
		}

		private void ApplyInteractionMode(bool gameMode)
		{
			cursorLocked = gameMode;
			cursorInputForLook = gameMode;
			if (!gameMode)
			{
				look = Vector2.zero;
			}

			SetCursorState(gameMode);
		}

		private static bool WasEscapePressedThisFrame()
		{
#if ENABLE_INPUT_SYSTEM
			return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
			return Input.GetKeyDown(KeyCode.Escape);
#endif
		}

		public void MoveInput(Vector2 newMoveDirection)
		{
			move = newMoveDirection;
		} 

		public void LookInput(Vector2 newLookDirection)
		{
			look = newLookDirection;
		}

		public void JumpInput(bool newJumpState)
		{
			jump = newJumpState;
		}

		public void SprintInput(bool newSprintState)
		{
			sprint = newSprintState;
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			if (hasFocus)
			{
				ApplyInteractionMode(!IsUiInteractionMode);
			}
		}

		private void SetCursorState(bool newState)
		{
			Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
			Cursor.visible = !newState;  
			

		}
	}
	
}