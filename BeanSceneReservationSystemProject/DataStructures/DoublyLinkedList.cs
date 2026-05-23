using System.Collections;

namespace BeanSceneReservationSystemProject.DataStructures
{
    public class DoublyLinkedList<T> : IEnumerable<T>
    {
        private sealed class Node
        {
            public Node(T value)
            {
                Value = value;
            }

            public T Value { get; }

            public Node? Previous { get; set; }

            public Node? Next { get; set; }
        }

        private Node? _head;
        private Node? _tail;

        public void AddLast(T value)
        {
            var node = new Node(value);
            if (_head == null)
            {
                _head = node;
                _tail = node;
                return;
            }

            node.Previous = _tail;
            _tail!.Next = node;
            _tail = node;
        }

        public bool ContainsForwardStep(T current, T next)
        {
            var comparer = EqualityComparer<T>.Default;
            for (var node = _head; node != null; node = node.Next)
            {
                if (comparer.Equals(node.Value, current))
                {
                    return node.Next != null && comparer.Equals(node.Next.Value, next);
                }
            }

            return false;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (var node = _head; node != null; node = node.Next)
            {
                yield return node.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
