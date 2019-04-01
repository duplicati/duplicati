var expect = require("chai").expect;

describe("binarysearch", function() {
  var bs  = require("./"),
      arr = [1, 2, 2, 2, 3, 5, 9],
      cmp = function(a, b) { return a - b; };

  it("should bail if not passed an array", function() {
    expect(function() { bs(undefined, 3, cmp); }).to.throw(TypeError);
  });

  it("should bail if not passed a comparator", function() {
    expect(function() { bs(arr, 3, undefined); }).to.throw(TypeError);
  });

  it("should return the index of an item in a sorted array", function() {
    expect(bs(arr, 3, cmp)).to.equal(4);
  });

  it("should return the index of where the item would go plus one, negated, if the item is not found", function() {
    expect(bs(arr, 4, cmp)).to.equal(-6);
  });

  it("should return any valid index if an item exists multiple times in the array", function() {
    expect(bs(arr, 2, cmp)).to.equal(3);
  });

  it("should work even on empty arrays", function() {
    expect(bs([], 42, cmp)).to.equal(-1);
  });

  it("should work even on arrays of doubles", function() {
    expect(bs([0.0, 0.1, 0.2, 0.3, 0.4], 0.25, cmp)).to.equal(-4);
  });

  it("should pass the index and array parameters to the comparator", function() {
    var indexes = [],
        indexCmp = function(a, b, i, array) {
          expect(array).to.equal(arr);
          indexes.push(i);
          return cmp(a, b);
        };
    bs(arr, 3, indexCmp);
    expect(indexes).to.deep.equal([3, 5, 4])
  });
});
