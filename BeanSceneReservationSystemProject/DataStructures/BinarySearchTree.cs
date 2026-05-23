using System.Collections;

namespace BeanSceneReservationSystemProject.DataStructures
{
    public class BinarySearchTree<T> : IEnumerable<T>
    {
        private sealed class Node
        {
            public Node(T value)
            {
                Value = value;
            }

            public T Value { get; }

            public Node? Left { get; set; }

            public Node? Right { get; set; }
        }

        private readonly IComparer<T> _comparer;
        private Node? _root;

        public BinarySearchTree(IComparer<T> comparer)
        {
            _comparer = comparer;
        }

        public void Insert(T value)
        {
            if (_root == null)
            {
                _root = new Node(value);
                return;
            }

            Insert(_root, value);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return TraverseInOrder(_root).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void Insert(Node node, T value)
        {
            if (_comparer.Compare(value, node.Value) < 0)
            {
                if (node.Left == null)
                {
                    node.Left = new Node(value);
                    return;
                }

                Insert(node.Left, value);
                return;
            }

            if (node.Right == null)
            {
                node.Right = new Node(value);
                return;
            }

            Insert(node.Right, value);
        }

        private static IEnumerable<T> TraverseInOrder(Node? node)
        {
            if (node == null)
            {
                yield break;
            }

            foreach (var value in TraverseInOrder(node.Left))
            {
                yield return value;
            }

            yield return node.Value;

            foreach (var value in TraverseInOrder(node.Right))
            {
                yield return value;
            }
        }
    }
}
