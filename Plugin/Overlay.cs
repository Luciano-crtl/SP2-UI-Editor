using Jundroo.Juicy.Widgets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SP2UIEditor
{
    // The outlines drawn over the game (a canvas of our own, above everything): blue = the selected element, yellow = the one under the mouse.
    internal static class Overlay
    {
        private static GameObject root;
        private static RawImage blocker;
        private static readonly RectTransform[] selected = new RectTransform[4], hover = new RectTransform[4];
        public static bool Hide;      // a picture of the game is being taken

        public static void Tick(bool connected)
        {
            if (!connected || Hide)
            {
                if (root != null && root.activeSelf) root.SetActive(false);
                return;
            }
            if (root == null) Make();
            if (!root.activeSelf) root.SetActive(true);
            if (blocker.gameObject.activeSelf != Picker.Active) blocker.gameObject.SetActive(Picker.Active);
            Place(selected, Picker.Selected);
            Place(hover, Picker.Active ? Picker.Hover : null);
        }

        private static void Make()
        {
            root = new GameObject("SP2UIEditor overlay");
            Object.DontDestroyOnLoad(root);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            root.AddComponent<GraphicRaycaster>();
            // While an element is being picked the click must not reach the game's buttons.
            blocker = Image("blocker", new Color(0, 0, 0, 0), true);
            RectTransform rt = blocker.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                selected[i] = Image("selected", new Color(0.2f, 0.6f, 1f, 1f), false).rectTransform;
                hover[i] = Image("hover", new Color(1f, 0.85f, 0.2f, 1f), false).rectTransform;
            }
        }

        private static RawImage Image(string name, Color color, bool raycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            RawImage i = go.AddComponent<RawImage>();
            i.texture = Texture2D.whiteTexture;
            i.color = color;
            i.raycastTarget = raycast;
            RectTransform rt = i.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            return i;
        }

        private static void Place(RectTransform[] bars, Widget w)
        {
            Rect r = default(Rect);
            bool show = w != null && w.gameObject.activeInHierarchy && Tree.ScreenRect(w, out r);
            for (int i = 0; i < 4; i++) if (bars[i].gameObject.activeSelf != show) bars[i].gameObject.SetActive(show);
            if (!show) return;
            const float t = 2f;
            Bar(bars[0], r.xMin - t, r.yMin - t, r.width + 2 * t, t);
            Bar(bars[1], r.xMin - t, r.yMax, r.width + 2 * t, t);
            Bar(bars[2], r.xMin - t, r.yMin, t, r.height);
            Bar(bars[3], r.xMax, r.yMin, t, r.height);
        }

        private static void Bar(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Destroy()
        {
            if (root != null) Object.Destroy(root);
            root = null;
        }
    }

    // Choosing an element with the mouse, in the game.
    internal static class Picker
    {
        public static bool Active;
        public static Widget Hover, Selected;
        public static int Serial;      // grows with every selection, so the page knows
        private static float nextLook;

        public static void Select(Widget w)
        {
            Selected = w;
            Serial++;
        }

        public static void Tick()
        {
            if (!Active) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Active = false;
                Hover = null;
                return;
            }
            if (Mouse.current == null) return;
            bool click = Mouse.current.leftButton.wasPressedThisFrame;
            if (click || Time.unscaledTime >= nextLook)
            {
                nextLook = Time.unscaledTime + 0.1f;   // looking through every widget takes a few milliseconds
                Hover = Tree.At(Mouse.current.position.ReadValue());
            }
            if (Mouse.current.leftButton.wasReleasedThisFrame && Hover != null)
            {
                // on release, so that the blocker still holds the whole click
                Select(Hover);
                Active = false;
                Hover = null;
            }
        }
    }
}
