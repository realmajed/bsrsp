using BeanSceneReservationSystemProject.DataStructures;
using BeanSceneReservationSystemProject.Models;

namespace BeanSceneReservationSystemProject.Services
{
    public static class TableOrdering
    {
        public static List<RestaurantTable> OrderTables(IEnumerable<RestaurantTable> tables)
        {
            var tree = new BinarySearchTree<RestaurantTable>(Comparer<RestaurantTable>.Create(CompareTables));

            foreach (var table in tables)
            {
                tree.Insert(table);
            }

            return tree.ToList();
        }

        private static int CompareTables(RestaurantTable? left, RestaurantTable? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            var areaComparison = string.Compare(left.Area?.AreaName, right.Area?.AreaName, StringComparison.OrdinalIgnoreCase);
            if (areaComparison != 0) return areaComparison;

            var prefixComparison = string.Compare(GetTableCodePrefix(left.TableCode), GetTableCodePrefix(right.TableCode), StringComparison.OrdinalIgnoreCase);
            if (prefixComparison != 0) return prefixComparison;

            var numberComparison = GetTableCodeNumber(left.TableCode).CompareTo(GetTableCodeNumber(right.TableCode));
            if (numberComparison != 0) return numberComparison;

            var codeComparison = string.Compare(left.TableCode, right.TableCode, StringComparison.OrdinalIgnoreCase);
            if (codeComparison != 0) return codeComparison;

            return left.RestaurantTableId.CompareTo(right.RestaurantTableId);
        }

        private static string GetTableCodePrefix(string tableCode)
        {
            var firstDigitIndex = tableCode.TakeWhile(c => !char.IsDigit(c)).Count();
            return tableCode[..firstDigitIndex];
        }

        private static int GetTableCodeNumber(string tableCode)
        {
            var digits = new string(tableCode
                .SkipWhile(c => !char.IsDigit(c))
                .TakeWhile(char.IsDigit)
                .ToArray());

            return int.TryParse(digits, out var number) ? number : int.MaxValue;
        }
    }
}
