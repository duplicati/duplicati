//Typescript type definition for:
//https://github.com/darkskyapp/binary-search
declare module 'binary-search' {

function binarySearch<A, B>(
  haystack: ArrayLike<A>,
  needle: B,
  comparator: (a: A, b: B, index?: number, haystack?: A[]) => any,
  // Notes about comparator return value:
  // * when a<b the comparator's returned value should be:
  //   * negative number or a value such that `+value` is a negative number
  //   * examples: `-1` or the string `"-1"`
  // * when a>b the comparator's returned value should be:
  //   * positive number or a value such that `+value` is a positive number
  //   * examples: `1` or the string `"1"`
  // * when a===b
  //    * any value other than the return cases for a<b and a>b
  //    * examples: undefined, NaN, 'abc'
  low?: number,
  high?: number): number; //returns index of found result or number < 0 if not found
export = binarySearch;
}
