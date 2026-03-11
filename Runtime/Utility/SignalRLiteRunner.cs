using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SignalRLite.Utility
{
    /// <summary>
    /// Singleton MonoBehaviour that drives coroutines and per-frame updates for all HubConnection instances.
    /// Created automatically on first use; persists across scene loads.
    /// </summary>
    public class SignalRLiteRunner : MonoBehaviour
    {
        private static SignalRLiteRunner _instance;

        public static SignalRLiteRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[SignalRLiteRunner]");
                    _instance = go.AddComponent<SignalRLiteRunner>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private readonly List<Action> _updateCallbacks = new List<Action>();
        private readonly List<Action> _pendingAdd = new List<Action>();
        private readonly List<Action> _pendingRemove = new List<Action>();
        private bool _isUpdating;

        public void RegisterUpdate(Action callback)
        {
            if (_isUpdating)
                _pendingAdd.Add(callback);
            else
                _updateCallbacks.Add(callback);
        }

        public void UnregisterUpdate(Action callback)
        {
            if (_isUpdating)
                _pendingRemove.Add(callback);
            else
                _updateCallbacks.Remove(callback);
        }

        private void Update()
        {
            _isUpdating = true;
            for (int i = 0; i < _updateCallbacks.Count; i++)
            {
                try { _updateCallbacks[i]?.Invoke(); }
                catch (Exception ex) { Debug.LogError($"[SignalRLite] Update error: {ex}"); }
            }
            _isUpdating = false;

            foreach (var a in _pendingRemove) _updateCallbacks.Remove(a);
            foreach (var a in _pendingAdd)    _updateCallbacks.Add(a);
            _pendingAdd.Clear();
            _pendingRemove.Clear();
        }

        public new Coroutine StartCoroutine(IEnumerator routine) => base.StartCoroutine(routine);
    }
}
