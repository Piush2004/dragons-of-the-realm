using UnityEngine;
namespace BlockRealm.Game
{
    public sealed class Probe : MonoBehaviour
    {
        private void Update()
        {
            Vector3 p = transform.position;
            transform.position = p + Vector3.up * Time.deltaTime;
            bool k = Input.GetKey(KeyCode.W);
            if (k) Debug.Log(Input.GetAxis("Mouse X"));
        }
        private void OnGUI() { GUI.Label(new Rect(0, 0, 100, 20), "hi"); }
    }
}
